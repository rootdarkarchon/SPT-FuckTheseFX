param([string]$SptInstallPath = 'D:\Tarkov-SPT-4.1')
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $SptInstallPath 'BepInEx/core/Mono.Cecil.dll')
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $SptInstallPath 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))
try {
    $prism = $game.MainModule.Types | Where-Object Name -eq 'PrismEffects'
    $methods = @($prism.Methods | Where-Object Name -eq 'HandleExposure')
    if ($methods.Count -ne 1 -or $methods[0].IsStatic -or $methods[0].ReturnType.FullName -ne 'System.Void' -or
        ($methods[0].Parameters.ParameterType.FullName -join '|') -ne 'UnityEngine.RenderTexture|UnityEngine.RenderTextureFormat') { throw 'Prism exposure signature changed' }
    $i = $methods[0].Body.Instructions
    $expected = 'ldarg.1|callvirt|ldc.i4.2|div|ldarg.1|callvirt|ldc.i4.2|div|ldc.i4.0|ldarg.2|call'
    if (($i[0..10].OpCode.Name -join '|') -ne $expected -or $i[1].Operand.FullName -ne 'System.Int32 UnityEngine.Texture::get_width()' -or
        $i[5].Operand.FullName -ne $i[1].Operand.FullName -or
        $i[10].Operand.FullName -ne 'UnityEngine.RenderTexture UnityEngine.RenderTexture::GetTemporary(System.Int32,System.Int32,System.Int32,UnityEngine.RenderTextureFormat)') { throw 'First square half-width exposure allocation changed' }
    $wrapper = $prism.Methods | Where-Object Name -eq 'OnRenderImage'
    $calls = @($wrapper.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] })
    $redirect = @($calls | Where-Object { $_.Operand.Name -eq 'GetSourceDestinationHDRToLDR' })
    $render = @($calls | Where-Object { $_.Operand.Name -eq 'OnRenderImageInternal' })
    if ($redirect.Count -ne 1 -or $render.Count -ne 1 -or $redirect[0].Offset -ge $render[0].Offset) { throw 'Prism output-size source redirection changed' }
    $sharpen = $game.MainModule.Types | Where-Object Name -eq 'CC_Sharpen'
    if (-not $sharpen -or -not ($sharpen.Methods | Where-Object { $_.Name -eq 'OnRenderImage' -and $_.HasBody })) { throw 'Sharpen image effect contract missing' }
    Write-Output 'Verified installed Prism exposure allocation, source redirection and CC_Sharpen contracts.'
} finally { $game.Dispose() }

$assemblies=@{}
try {
    foreach($name in @('Assembly-CSharp','Assembly-CSharp-firstpass','Unity.Postprocessing.Runtime')) {
        $assemblies[$name]=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $SptInstallPath "EscapeFromTarkov_Data/Managed/$name.dll"))
    }
    $ids=@{'Assembly-CSharp'='cc2d80b0-6d5b-4cb1-a581-6d2cc901d4c7';'Assembly-CSharp-firstpass'='79ed6e2f-9190-49a0-a4ee-138078f70dc9';'Unity.Postprocessing.Runtime'='bb1716ca-81b5-4288-9a04-95ef431a6ddf'}
    foreach($name in $ids.Keys) { if($assemblies[$name].MainModule.Mvid -ne [guid]$ids[$name]) { throw "Inactive-effects contract changed: $name" } }
    foreach($name in @('DeathFade','BloodOnScreen','PostprocessGrayscale','DistortCameraFX','InfectionEffect')) {
        $type=$assemblies['Assembly-CSharp'].MainModule.Types | Where-Object Name -eq $name
        $wrapper=$type.Methods | Where-Object Name -eq 'OnRenderImage'
        $calls=@($wrapper.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] })
        $get=@($calls | Where-Object { $_.Operand.DeclaringType.Name -eq 'SSAAPropagator' -and $_.Operand.Name -eq 'GetSourceDestination' })
        $release=@($calls | Where-Object { $_.Operand.DeclaringType.Name -eq 'SSAAPropagator' -and $_.Operand.Name -eq 'ReleaseSourceDestination' })
        if($get.Count -ne 1 -or $release.Count -ne 1 -or $get[0].Offset -ge $release[0].Offset) {throw "Status image handoff changed: $name"}
    }
    $dof=$assemblies['Assembly-CSharp-firstpass'].MainModule.Types | Where-Object FullName -eq 'UnityStandardAssets.ImageEffects.DepthOfField'
    $method=$dof.Methods | Where-Object Name -eq 'OnRenderImage'
    $sites=@($method.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'WriteCoc' -and
        $_.Previous.OpCode.Name -eq 'ldc.i4.1' -and $_.Next.OpCode.Name -eq 'ldarg.1' -and $_.Next.Next.Operand.Name -eq 'get_width' })
    if($sites.Count -ne 1 -or $sites[0].Previous.OpCode.Name -ne 'ldc.i4.1') {throw 'Native DoF CoC-to-disc allocation boundary changed'}
    $prop=$assemblies['Unity.Postprocessing.Runtime'].MainModule.Types | Where-Object Name -eq 'SSAAPropagator'
    foreach($name in @('GetSourceDestination','GetSourceDestinationHDR','GetSourceDestinationLDR')) {
        $get=$prop.Methods | Where-Object Name -eq $name
        if($get.Body.Instructions | Where-Object {$_.OpCode.Name -match '^stfld|^stsfld'}) {throw 'Native SSAA getter mutates chain state'}
    }
    $impl=$assemblies['Unity.Postprocessing.Runtime'].MainModule.Types | Where-Object Name -eq 'SSAAImpl'
    $final=$impl.Methods | Where-Object Name -eq 'OnRenderImage'
    if(-not ($final.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'GetSourceDestination' })) {throw 'Final image consumer no longer reads SSAA current texture'}
    Write-Output 'Verified exact inactive predicates, five status handoffs, pure SSAA selectors/final consumer and native DoF CoC boundary through pinned module contracts.'
} finally { foreach($assembly in $assemblies.Values) { $assembly.Dispose() } }
