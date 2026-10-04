// Native D3D11 executor for the upstream NRD DXBC dispatch list. No algorithm port.
#include <windows.h>
#include <d3d11.h>
#include <wrl/client.h>
#include <array>
#include <vector>
#include <memory>
#include <mutex>
#include <unordered_map>
#include <stdexcept>
#include <cstring>
#include <atomic>
#include <string>
#include <chrono>
#include <cmath>
#include "NRD.h"
#include "Pack.h"
#include "Unpack.h"
using Microsoft::WRL::ComPtr;
#define API extern "C" __declspec(dllexport)
namespace {
struct Frame {
    float projection[16], previousProjection[16], view[16], previousView[16];
    float jitter[2], previousJitter[2];
    uint32_t reset, frames, frameIndex, antiFirefly;float deltaMs;
};
static_assert(sizeof(Frame)==292,"Managed frame ABI");
struct DiffuseFrame { Frame camera; uint32_t version; float radius; uint32_t validation; };
static_assert(sizeof(DiffuseFrame)==304,"Diffuse frame ABI");
struct Stats { uint64_t permanentBytes, transientBytes, adapterBytes, frames, dispatches, failures; int32_t status, pending;double queueUs,eventUs; };
static_assert(sizeof(Stats)==72,"Managed statistics ABI");
using Clock=std::chrono::steady_clock;
double Us(Clock::time_point start){return std::chrono::duration<double,std::micro>(Clock::now()-start).count();}
void Check(HRESULT result) { if(FAILED(result))throw std::runtime_error("D3D11 operation failed HRESULT="+std::to_string(uint32_t(result))); }
void Check(nrd::Result result) { if(result!=nrd::Result::SUCCESS)throw std::runtime_error("NRD operation failed"); }
DXGI_FORMAT Format(nrd::Format f) {
    switch(f) {
    case nrd::Format::R8_UNORM:return DXGI_FORMAT_R8_UNORM;
    case nrd::Format::R8_UINT:return DXGI_FORMAT_R8_UINT;
    case nrd::Format::R16_UINT:return DXGI_FORMAT_R16_UINT;
    case nrd::Format::R16_SFLOAT:return DXGI_FORMAT_R16_FLOAT;
    case nrd::Format::R32_UINT:return DXGI_FORMAT_R32_UINT;
    case nrd::Format::R32_SFLOAT:return DXGI_FORMAT_R32_FLOAT;
    case nrd::Format::RG16_SFLOAT:return DXGI_FORMAT_R16G16_FLOAT;
    case nrd::Format::RGBA16_SFLOAT:return DXGI_FORMAT_R16G16B16A16_FLOAT;
    case nrd::Format::RGBA8_UNORM:return DXGI_FORMAT_R8G8B8A8_UNORM;
    case nrd::Format::R10_G10_B10_A2_UNORM:return DXGI_FORMAT_R10G10B10A2_UNORM;
    default:throw std::runtime_error("Unsupported NRD pool format");
    }
}
uint32_t Bytes(DXGI_FORMAT f) {
    switch(f) {
    case DXGI_FORMAT_R8_UNORM:case DXGI_FORMAT_R8_UINT:return 1;
    case DXGI_FORMAT_R16_UINT:case DXGI_FORMAT_R16_FLOAT:return 2;
    case DXGI_FORMAT_R16G16B16A16_FLOAT:return 8;
    default:return 4;
    }
}
DXGI_FORMAT Typed(DXGI_FORMAT format) {
    switch(format) {
    case DXGI_FORMAT_R16G16B16A16_TYPELESS:return DXGI_FORMAT_R16G16B16A16_FLOAT;
    case DXGI_FORMAT_R32G32B32A32_TYPELESS:return DXGI_FORMAT_R32G32B32A32_FLOAT;
    case DXGI_FORMAT_R16G16_TYPELESS:return DXGI_FORMAT_R16G16_FLOAT;
    case DXGI_FORMAT_R32_TYPELESS:return DXGI_FORMAT_R32_FLOAT;
    case DXGI_FORMAT_R10G10B10A2_TYPELESS:return DXGI_FORMAT_R10G10B10A2_UNORM;
    case DXGI_FORMAT_R8G8B8A8_TYPELESS:return DXGI_FORMAT_R8G8B8A8_UNORM;
    default:return format;
    }
}
struct Texture {
    ComPtr<ID3D11Texture2D> texture; ComPtr<ID3D11ShaderResourceView> srv; ComPtr<ID3D11UnorderedAccessView> uav;
    uint64_t bytes=0;
    void Init(ID3D11Device* device,UINT w,UINT h,DXGI_FORMAT format) {
        D3D11_TEXTURE2D_DESC d{};d.Width=w;d.Height=h;d.MipLevels=d.ArraySize=1;d.Format=format;d.SampleDesc.Count=1;
        d.BindFlags=D3D11_BIND_SHADER_RESOURCE|D3D11_BIND_UNORDERED_ACCESS;
        Check(device->CreateTexture2D(&d,nullptr,&texture));
        Check(device->CreateShaderResourceView(texture.Get(),nullptr,&srv));
        Check(device->CreateUnorderedAccessView(texture.Get(),nullptr,&uav));bytes=uint64_t(w)*h*Bytes(format);
    }
};
// NRD changes compute bindings only. Preserve all compute slots and unbind
// pixel inputs / OM targets while NRD writes Unity resources, then restore.
struct StateGuard {
    ID3D11DeviceContext* c;
    std::array<ID3D11ShaderResourceView*,128> cs{},ps{};
    std::array<ID3D11UnorderedAccessView*,8> uav{};
    std::array<ID3D11SamplerState*,16> samplers{};
    std::array<ID3D11Buffer*,14> constants{};
    std::array<ID3D11RenderTargetView*,8> targets{};
    std::array<ID3D11UnorderedAccessView*,8> omUav{};
    ID3D11DepthStencilView* depth=nullptr;ID3D11ComputeShader* shader=nullptr;
    std::array<ID3D11ClassInstance*,256> classes{};UINT classCount=256;
    explicit StateGuard(ID3D11DeviceContext* context):c(context) {
        c->CSGetShaderResources(0,128,cs.data());c->PSGetShaderResources(0,128,ps.data());
        c->CSGetUnorderedAccessViews(0,8,uav.data());c->CSGetSamplers(0,16,samplers.data());c->CSGetConstantBuffers(0,14,constants.data());
        c->CSGetShader(&shader,classes.data(),&classCount);c->OMGetRenderTargets(8,targets.data(),&depth);
        c->OMGetRenderTargetsAndUnorderedAccessViews(0,nullptr,nullptr,0,8,omUav.data());
        std::array<ID3D11ShaderResourceView*,128> nullSrv{};std::array<ID3D11UnorderedAccessView*,8> nullUav{};
        c->CSSetShaderResources(0,128,nullSrv.data());c->PSSetShaderResources(0,128,nullSrv.data());
        c->CSSetUnorderedAccessViews(0,8,nullUav.data(),nullptr);c->OMSetRenderTargetsAndUnorderedAccessViews(0,nullptr,nullptr,0,8,nullUav.data(),nullptr);
    }
    template<class T,size_t N> static void Release(std::array<T*,N>& values){for(auto* p:values)if(p)p->Release();}
    ~StateGuard() {
        std::array<ID3D11ShaderResourceView*,128> nullSrv{};std::array<ID3D11UnorderedAccessView*,8> nullUav{};
        c->CSSetShaderResources(0,128,nullSrv.data());c->CSSetUnorderedAccessViews(0,8,nullUav.data(),nullptr);
        UINT rtCount=0;for(UINT i=0;i<8;i++)if(targets[i])rtCount=i+1;
        c->OMSetRenderTargets(rtCount,targets.data(),depth);
        if(rtCount<8)c->OMSetRenderTargetsAndUnorderedAccessViews(D3D11_KEEP_RENDER_TARGETS_AND_DEPTH_STENCIL,nullptr,nullptr,rtCount,8-rtCount,omUav.data()+rtCount,nullptr);
        c->PSSetShaderResources(0,128,ps.data());
        c->CSSetShader(shader,classes.data(),classCount);c->CSSetSamplers(0,16,samplers.data());c->CSSetConstantBuffers(0,14,constants.data());
        c->CSSetShaderResources(0,128,cs.data());c->CSSetUnorderedAccessViews(0,8,uav.data(),nullptr);
        Release(cs);Release(ps);Release(uav);Release(omUav);Release(samplers);Release(constants);Release(targets);Release(classes);
        if(depth)depth->Release();if(shader)shader->Release();
    }
};
// Diffuse views are immutable per resource generation. Packets copy COM ownership,
// so replacing this cache cannot invalidate queued work during resize/disposal.
struct DiffuseViews {
    std::array<ComPtr<ID3D11Resource>,6> resources;
    std::array<ComPtr<ID3D11ShaderResourceView>,4> inputs;
    ComPtr<ID3D11ShaderResourceView> outputRead;
    ComPtr<ID3D11UnorderedAccessView> output,motionWrite,validation;
    bool Matches(void* const* textures)const {
        for(size_t i=0;i<resources.size();i++)if(resources[i].Get()!=textures[i])return false;
        return true;
    }
    explicit DiffuseViews(void* const* textures) {
        for(size_t i=0;i<resources.size();i++)resources[i]=static_cast<ID3D11Resource*>(textures[i]);
        ComPtr<ID3D11Device> device;resources[0]->GetDevice(&device);
        auto srv=[&](size_t i,ComPtr<ID3D11ShaderResourceView>& result) {
            D3D11_TEXTURE2D_DESC d{};static_cast<ID3D11Texture2D*>(resources[i].Get())->GetDesc(&d);
            D3D11_SHADER_RESOURCE_VIEW_DESC v{};v.Format=Typed(d.Format);v.ViewDimension=D3D11_SRV_DIMENSION_TEXTURE2D;v.Texture2D.MipLevels=1;
            Check(device->CreateShaderResourceView(resources[i].Get(),&v,&result));
        };
        auto uav=[&](size_t i,ComPtr<ID3D11UnorderedAccessView>& result) {
            D3D11_TEXTURE2D_DESC d{};static_cast<ID3D11Texture2D*>(resources[i].Get())->GetDesc(&d);
            D3D11_UNORDERED_ACCESS_VIEW_DESC v{};v.Format=Typed(d.Format);v.ViewDimension=D3D11_UAV_DIMENSION_TEXTURE2D;
            Check(device->CreateUnorderedAccessView(resources[i].Get(),&v,&result));
        };
        for(size_t i=0;i<4;i++)srv(i,inputs[i]);
        srv(4,outputRead);uav(4,output);uav(3,motionWrite);if(resources[5])uav(5,validation);
    }
};
struct Backend {
    nrd::Instance* instance=nullptr;uint32_t width,height;ComPtr<ID3D11Device> device;
    std::vector<Texture> permanent,transient;std::vector<ComPtr<ID3D11ComputeShader>> shaders;
    Texture normal,z,motion,colour,coverage,outColour,outCoverage;
    ComPtr<ID3D11ComputeShader> pack,unpack;ComPtr<ID3D11Buffer> cb,adapterCb;
    std::array<ComPtr<ID3D11SamplerState>,2> samplers;
    Stats stats{};
    std::string error; bool diffuse=false;
    std::unique_ptr<DiffuseViews> diffuseViews;uint64_t viewBuilds=0;
    Backend(uint32_t w,uint32_t h,bool diff=false):width(w),height(h),diffuse(diff) {
        nrd::DenoiserDesc methods[]={{1,nrd::Denoiser::REBLUR_SPECULAR},{2,nrd::Denoiser::REBLUR_SPECULAR}};
        nrd::InstanceCreationDesc d{};d.denoisers=methods;d.denoisersNum=diffuse?1:2;if(diffuse)methods[0].denoiser=nrd::Denoiser::REBLUR_DIFFUSE;Check(nrd::CreateInstance(d,instance));
    }
    ~Backend(){if(instance)nrd::DestroyInstance(*instance);}
    void Init(ID3D11Device* dev) {
        device=dev;const auto& d=*nrd::GetInstanceDesc(*instance);
        for(UINT i=0;i<d.pipelinesNum;i++) {
            const auto& code=d.pipelines[i].computeShaderDXBC;
            if(!code.bytecode||code.size<4||memcmp(code.bytecode,"DXBC",4)!=0)throw std::runtime_error("NRD shader is not DXBC");
            ComPtr<ID3D11ComputeShader> s;Check(dev->CreateComputeShader(code.bytecode,size_t(code.size),nullptr,&s));shaders.push_back(s);
        }
        auto pools=[&](const nrd::TextureDesc* desc,UINT count,std::vector<Texture>& dest,uint64_t& bytes) {
            dest.resize(count);for(UINT i=0;i<count;i++) {
                UINT scale=desc[i].downsampleFactor;dest[i].Init(dev,(width+scale-1)/scale,(height+scale-1)/scale,Format(desc[i].format));bytes+=dest[i].bytes;
            }
        };
        pools(d.permanentPool,d.permanentPoolSize,permanent,stats.permanentBytes);pools(d.transientPool,d.transientPoolSize,transient,stats.transientBytes);
        if(!diffuse) { normal.Init(dev,width,height,DXGI_FORMAT_R10G10B10A2_UNORM);z.Init(dev,width,height,DXGI_FORMAT_R32_FLOAT);
        motion.Init(dev,width,height,DXGI_FORMAT_R16G16B16A16_FLOAT);
        for(auto* t:{&colour,&coverage,&outColour,&outCoverage})t->Init(dev,width,height,DXGI_FORMAT_R16G16B16A16_FLOAT);
        for(auto* t:{&normal,&z,&motion,&colour,&coverage,&outColour,&outCoverage})stats.adapterBytes+=t->bytes;
        Check(dev->CreateComputeShader(FTFX_Pack,sizeof(FTFX_Pack),nullptr,&pack));Check(dev->CreateComputeShader(FTFX_Unpack,sizeof(FTFX_Unpack),nullptr,&unpack));
        }
        D3D11_BUFFER_DESC b{};b.ByteWidth=(d.constantBufferMaxDataSize+15)&~15u;b.BindFlags=D3D11_BIND_CONSTANT_BUFFER;
        Check(dev->CreateBuffer(&b,nullptr,&cb));b.ByteWidth=16;Check(dev->CreateBuffer(&b,nullptr,&adapterCb));
        for(UINT i=0;i<2;i++) {
            D3D11_SAMPLER_DESC s{};s.Filter=i?D3D11_FILTER_MIN_MAG_MIP_LINEAR:D3D11_FILTER_MIN_MAG_MIP_POINT;
            s.AddressU=s.AddressV=s.AddressW=D3D11_TEXTURE_ADDRESS_CLAMP;s.MaxLOD=D3D11_FLOAT32_MAX;
            Check(dev->CreateSamplerState(&s,&samplers[i]));
        }
        stats.status=1;
    }
    Texture& Resource(const nrd::ResourceDesc& r,nrd::Identifier id) {
        switch(r.type) {
        case nrd::ResourceType::IN_MV:return motion;
        case nrd::ResourceType::IN_NORMAL_ROUGHNESS:return normal;
        case nrd::ResourceType::IN_VIEWZ:return z;
        case nrd::ResourceType::IN_SPEC_RADIANCE_HITDIST:return id==1?colour:coverage;
        case nrd::ResourceType::OUT_SPEC_RADIANCE_HITDIST:return id==1?outColour:outCoverage;
        case nrd::ResourceType::PERMANENT_POOL:return permanent.at(r.indexInPool);
        case nrd::ResourceType::TRANSIENT_POOL:return transient.at(r.indexInPool);
        default:throw std::runtime_error("Unexpected NRD dispatch resource");
        }
    }
};
struct Packet {
    uintptr_t owner=0;std::shared_ptr<Backend> backend;Frame frame{};
    std::array<ComPtr<ID3D11ShaderResourceView>,4> inputs;ComPtr<ID3D11ShaderResourceView> outputRead;
    ComPtr<ID3D11UnorderedAccessView> output,validation,motionWrite; float radius=1; bool validate=false;
};
std::mutex gate;uintptr_t serial=0;
std::unordered_map<uintptr_t,std::shared_ptr<Backend>> backends;
std::unordered_map<uintptr_t,std::unique_ptr<Packet>> packets;
void NullBindings(ID3D11DeviceContext* c) {
    std::array<ID3D11ShaderResourceView*,32> s{};std::array<ID3D11UnorderedAccessView*,8> u{};
    c->CSSetShaderResources(0,32,s.data());c->CSSetUnorderedAccessViews(0,8,u.data(),nullptr);
}
void Run(Packet& p) {
    auto& b=*p.backend;ComPtr<ID3D11Device> dev;p.inputs[0]->GetDevice(&dev);
    if(!b.device)b.Init(dev.Get());if(b.device.Get()!=dev.Get())throw std::runtime_error("D3D11 device changed");
    ComPtr<ID3D11DeviceContext> c;dev->GetImmediateContext(&c);StateGuard saved(c.Get());
    ID3D11Buffer* adapter=b.adapterCb.Get();
    if(!b.diffuse) { uint32_t constants[]={b.width,b.height,0,0};c->UpdateSubresource(b.adapterCb.Get(),0,nullptr,constants,0,0);
    c->CSSetConstantBuffers(0,1,&adapter);c->CSSetShader(b.pack.Get(),nullptr,0);
    ID3D11ShaderResourceView* input[]={p.inputs[0].Get(),p.inputs[1].Get(),p.inputs[2].Get(),p.inputs[3].Get()};
    ID3D11UnorderedAccessView* targets[]={b.normal.uav.Get(),b.z.uav.Get(),b.motion.uav.Get(),b.colour.uav.Get(),b.coverage.uav.Get()};
    c->CSSetShaderResources(0,4,input);c->CSSetUnorderedAccessViews(0,5,targets,nullptr);c->Dispatch((b.width+7)/8,(b.height+7)/8,1);NullBindings(c.Get());
    }
    nrd::CommonSettings s{};
    memcpy(s.viewToClipMatrix,p.frame.projection,64);memcpy(s.viewToClipMatrixPrev,p.frame.previousProjection,64);
    memcpy(s.worldToViewMatrix,p.frame.view,64);memcpy(s.worldToViewMatrixPrev,p.frame.previousView,64);
    memcpy(s.cameraJitter,p.frame.jitter,8);memcpy(s.cameraJitterPrev,p.frame.previousJitter,8);
    s.resourceSize[0]=s.resourceSizePrev[0]=s.rectSize[0]=s.rectSizePrev[0]=uint16_t(b.width);
    s.resourceSize[1]=s.resourceSizePrev[1]=s.rectSize[1]=s.rectSizePrev[1]=uint16_t(b.height);
    s.frameIndex=p.frame.frameIndex;s.timeDeltaBetweenFrames=p.frame.deltaMs;s.denoisingRange=1e6f;
    s.enableValidation=b.diffuse && p.validate;
    s.accumulationMode=p.frame.reset||b.stats.frames==0?nrd::AccumulationMode::CLEAR_AND_RESTART:nrd::AccumulationMode::CONTINUE;
    Check(nrd::SetCommonSettings(*b.instance,s));nrd::ReblurSettings settings{};
    settings.maxAccumulatedFrameNum=p.frame.frames;settings.maxFastAccumulatedFrameNum=std::min(6u,p.frame.frames);
    settings.historyFixFrameNum=std::min(3u,settings.maxFastAccumulatedFrameNum?settings.maxFastAccumulatedFrameNum-1:0);
    settings.maxStabilizedFrameNum=p.frame.frames;settings.enableAntiFirefly=p.frame.antiFirefly!=0;
    settings.minMaterialForSpecular=0; // dry/water guide boundary, not sampled source class
    // Zero distance denotes unavailable geometry. No probabilistic hit-distance
    // reconstruction is enabled: SSR misses are not skipped GGX lobe samples.
    settings.hitDistanceReconstructionMode=nrd::HitDistanceReconstructionMode::OFF;
    if(b.diffuse) { settings.hitDistanceParameters.A=p.radius;settings.hitDistanceParameters.B=0;settings.hitDistanceParameters.C=1;
        settings.diffusePrepassBlurRadius=8;settings.maxBlurRadius=16;settings.minBlurRadius=1; }
    Check(nrd::SetDenoiserSettings(*b.instance,1,&settings));if(!b.diffuse)Check(nrd::SetDenoiserSettings(*b.instance,2,&settings));
    nrd::Identifier ids[]={1,2};const nrd::DispatchDesc* dispatches=nullptr;uint32_t count=0;
    Check(nrd::GetComputeDispatches(*b.instance,ids,b.diffuse?1:2,dispatches,count));const auto& desc=*nrd::GetInstanceDesc(*b.instance);
    ID3D11SamplerState* samplers[]={b.samplers[0].Get(),b.samplers[1].Get()};c->CSSetSamplers(desc.samplersBaseRegisterIndex,2,samplers);
    ID3D11Buffer* cb=b.cb.Get();c->CSSetConstantBuffers(desc.constantBufferRegisterIndex,1,&cb);
    std::vector<uint8_t> constantData((desc.constantBufferMaxDataSize+15)&~15u);
    for(UINT i=0;i<count;i++) {
        const auto& d=dispatches[i];NullBindings(c.Get());
        std::array<ID3D11ShaderResourceView*,32> srvs{};std::array<ID3D11UnorderedAccessView*,8> uavs{};UINT ns=0,nu=0;
        for(UINT j=0;j<d.resourcesNum;j++) {
            const auto& r=d.resources[j];
            if(b.diffuse) {
                int input=-1;
                switch(r.type) {
                case nrd::ResourceType::IN_DIFF_RADIANCE_HITDIST:input=0;break;
                case nrd::ResourceType::IN_NORMAL_ROUGHNESS:input=1;break;
                case nrd::ResourceType::IN_VIEWZ:input=2;break;
                case nrd::ResourceType::IN_MV:input=3;break;
                case nrd::ResourceType::OUT_DIFF_RADIANCE_HITDIST:
                    if(r.descriptorType==nrd::DescriptorType::TEXTURE)srvs.at(ns++)=p.outputRead.Get();else uavs.at(nu++)=p.output.Get();continue;
                case nrd::ResourceType::OUT_VALIDATION:uavs.at(nu++)=p.validation.Get();continue;
                default:break;
                }
                if(input>=0){
                    if(r.descriptorType==nrd::DescriptorType::TEXTURE)srvs.at(ns++)=p.inputs[input].Get();
                    else if(input==3)uavs.at(nu++)=p.motionWrite.Get();
                    else throw std::runtime_error("Unexpected writable diffuse input");
                    continue;
                }
            }
            auto& texture=b.Resource(r,d.identifier);
            if(r.descriptorType==nrd::DescriptorType::TEXTURE){if(ns>=32)throw std::runtime_error("SRV overflow");srvs[ns++]=texture.srv.Get();}
            else {if(nu>=8)throw std::runtime_error("UAV overflow");uavs[nu++]=texture.uav.Get();}
        }
        if(d.constantBufferDataSize){memcpy(constantData.data(),d.constantBufferData,d.constantBufferDataSize);c->UpdateSubresource(cb,0,nullptr,constantData.data(),0,0);}
        c->CSSetShaderResources(desc.resourcesBaseRegisterIndex,ns,srvs.data());c->CSSetUnorderedAccessViews(desc.resourcesBaseRegisterIndex,nu,uavs.data(),nullptr);
        c->CSSetShader(b.shaders.at(d.pipelineIndex).Get(),nullptr,0);c->Dispatch(d.gridWidth,d.gridHeight,1);
    }
    if(b.diffuse){NullBindings(c.Get());b.stats.frames++;b.stats.dispatches=count;return;}
    NullBindings(c.Get());c->CSSetShader(b.unpack.Get(),nullptr,0);c->CSSetConstantBuffers(0,1,&adapter);
    ID3D11ShaderResourceView* decoded[]={b.outColour.srv.Get(),b.outCoverage.srv.Get(),p.inputs[2].Get(),p.inputs[0].Get()};
    ID3D11UnorderedAccessView* out=p.output.Get();c->CSSetShaderResources(0,4,decoded);c->CSSetUnorderedAccessViews(0,1,&out,nullptr);
    c->Dispatch((b.width+7)/8,(b.height+7)/8,1);b.stats.frames++;b.stats.dispatches=count+2;
}
void __stdcall Event(int event,void* token) {
    std::lock_guard<std::mutex> lock(gate);if(event!=1)return;
    auto found=packets.find(reinterpret_cast<uintptr_t>(token));if(found==packets.end())return;
    auto packet=std::move(found->second);packets.erase(found);auto& b=*packet->backend;
    auto start=Clock::now();
    try{Run(*packet);}catch(const std::exception& e){b.error=e.what();b.stats.status=-1;b.stats.failures++;}
    b.stats.eventUs=Us(start);
    if(b.stats.status<0){
        ComPtr<ID3D11Device> dev;packet->output->GetDevice(&dev);ComPtr<ID3D11DeviceContext> c;dev->GetImmediateContext(&c);
        ComPtr<ID3D11Resource> source,target;packet->inputs[0]->GetResource(&source);packet->output->GetResource(&target);
        if(b.diffuse){float zero[4]={};c->ClearUnorderedAccessViewFloat(packet->output.Get(),zero);}
        else c->CopyResource(target.Get(),source.Get()); // SSR current-frame fallback only.
    }
}
}
API int __cdecl FTFX_NrdAbi(){return sizeof(Frame);}
API void* __cdecl FTFX_NrdCallback(){return reinterpret_cast<void*>(&Event);}
API uintptr_t __cdecl FTFX_NrdCreate(uint32_t w,uint32_t h) {
    std::lock_guard<std::mutex> lock(gate);if(!w||!h||w>65535||h>65535)return 0;
    try {auto token=++serial;backends[token]=std::make_shared<Backend>(w,h);return token;}catch(...){return 0;}
}
API void __cdecl FTFX_NrdDestroy(uintptr_t id) {
    std::lock_guard<std::mutex> lock(gate);backends.erase(id);
    // Queued packets retain independent ownership until their render callback.
}
API int __cdecl FTFX_NrdCancelOwner(uintptr_t id) {
    std::lock_guard<std::mutex> lock(gate);int count=0;
    for(auto p=packets.begin();p!=packets.end();) {
        if(p->second->owner==id){p=packets.erase(p);count++;}else ++p;
    }
    return count;
}
API uintptr_t __cdecl FTFX_NrdQueue(uintptr_t id,void* signal,void* normal,void* receiver,void* motion,void* output,const Frame* frame) {
    auto start=Clock::now();
    std::lock_guard<std::mutex> lock(gate);auto found=backends.find(id);if(found==backends.end())return 0;
    if(!frame||!signal||!normal||!receiver||!motion||!output){found->second->error="Native texture unavailable";found->second->stats.status=-1;found->second->stats.failures++;return 0;}
    try {
        auto p=std::make_unique<Packet>();p->owner=id;p->backend=found->second;p->frame=*frame;
        void* textures[]={signal,normal,receiver,motion};ComPtr<ID3D11Device> device;
        static_cast<ID3D11Resource*>(signal)->GetDevice(&device);
        for(UINT i=0;i<4;i++){
            if(!textures[i])return 0;D3D11_TEXTURE2D_DESC desc{};static_cast<ID3D11Texture2D*>(textures[i])->GetDesc(&desc);
            D3D11_SHADER_RESOURCE_VIEW_DESC srv{};srv.Format=Typed(desc.Format);srv.ViewDimension=D3D11_SRV_DIMENSION_TEXTURE2D;srv.Texture2D.MipLevels=1;
            HRESULT result=device->CreateShaderResourceView(static_cast<ID3D11Resource*>(textures[i]),&srv,&p->inputs[i]);
            if(FAILED(result))throw std::runtime_error("NRD input "+std::to_string(i)+" format "+std::to_string(desc.Format)+" SRV HRESULT="+std::to_string(uint32_t(result)));
        }
        D3D11_TEXTURE2D_DESC desc{};static_cast<ID3D11Texture2D*>(output)->GetDesc(&desc);
        D3D11_UNORDERED_ACCESS_VIEW_DESC uav{};uav.Format=Typed(desc.Format);uav.ViewDimension=D3D11_UAV_DIMENSION_TEXTURE2D;
        Check(device->CreateUnorderedAccessView(static_cast<ID3D11Resource*>(output),&uav,&p->output));
        auto token=++serial;packets[token]=std::move(p);found->second->stats.queueUs=Us(start);return token;
    }catch(const std::exception& e){found->second->error=e.what();found->second->stats.status=-1;found->second->stats.failures++;return 0;}
}
API void __cdecl FTFX_NrdUpdate(uintptr_t token,const Frame* frame) {
    std::lock_guard<std::mutex> lock(gate);auto p=packets.find(token);if(p!=packets.end())p->second->frame=*frame;
}
API void __cdecl FTFX_NrdCancel(uintptr_t token) {std::lock_guard<std::mutex> lock(gate);packets.erase(token);}
API int __cdecl FTFX_NrdStats(uintptr_t id,Stats* stats) {
    std::lock_guard<std::mutex> lock(gate);auto b=backends.find(id);if(b==backends.end())return 0;*stats=b->second->stats;
    for(auto& p:packets)if(p.second->backend==b->second)stats->pending++;return 1;
}
API void __cdecl FTFX_NrdError(uintptr_t id,char* text,uint32_t size) {
    std::lock_guard<std::mutex> lock(gate);auto b=backends.find(id);if(!size)return;
    const char* error=b==backends.end()?"missing instance":b->second->error.c_str();strncpy_s(text,size,error,_TRUNCATE);
}

API int __cdecl FTFX_NrdDiffuseAbi(){return sizeof(DiffuseFrame);}
API uintptr_t __cdecl FTFX_NrdDiffuseCreate(uint32_t w,uint32_t h,uint32_t version) {
    std::lock_guard<std::mutex> lock(gate);if(version!=1||!w||!h||w>65535||h>65535)return 0;
    try{auto id=++serial;backends[id]=std::make_shared<Backend>(w,h,true);return id;}catch(...){return 0;}
}
API uintptr_t __cdecl FTFX_NrdDiffuseQueue(uintptr_t id,void* radiance,void* normal,void* depth,void* motion,void* output,void* validation,const DiffuseFrame* f) {
    auto start=Clock::now();std::lock_guard<std::mutex> lock(gate);
    auto found=backends.find(id);if(found==backends.end()||!found->second->diffuse)return 0;auto& b=*found->second;
    if(!f||f->version!=1||!std::isfinite(f->radius)||f->radius<=0||f->camera.frames>63||!radiance||!normal||!depth||!motion||!output||(f->validation&&!validation))return 0;
    try {
        void* textures[]={radiance,normal,depth,motion,output,f->validation?validation:nullptr};
        if(!b.diffuseViews||!b.diffuseViews->Matches(textures)){b.diffuseViews=std::make_unique<DiffuseViews>(textures);b.viewBuilds++;}
        auto p=std::make_unique<Packet>();p->owner=id;p->backend=found->second;p->frame=f->camera;p->radius=f->radius;p->validate=f->validation!=0;
        const auto& views=*b.diffuseViews;p->inputs=views.inputs;p->output=views.output;p->outputRead=views.outputRead;p->motionWrite=views.motionWrite;p->validation=views.validation;
        auto token=++serial;packets[token]=std::move(p);b.stats.queueUs=Us(start);return token;
    }catch(const std::exception& e){b.error=e.what();b.stats.status=-1;b.stats.failures++;return 0;}
}
API uint64_t __cdecl FTFX_NrdDiffuseViewBuilds(uintptr_t id) {
    std::lock_guard<std::mutex> lock(gate);auto b=backends.find(id);return b==backends.end()?0:b->second->viewBuilds;
}
API void __cdecl FTFX_NrdDiffuseUpdate(uintptr_t token,const DiffuseFrame* f) {
    if(!f||f->version!=1)return;std::lock_guard<std::mutex> lock(gate);auto p=packets.find(token);
    if(p!=packets.end()){p->second->frame=f->camera;p->second->radius=f->radius;}
}
