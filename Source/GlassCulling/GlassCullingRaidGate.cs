using System;
using EFT;

namespace FuckTheseFX.Source.GlassCulling
{
    internal static class GlassCullingRaidGate
    {
        internal static bool IsReady(AbstractGame game, GameWorld world)
        {
            if (!game || !world || game.GameType == EGameType.Hideout) return false;
            if (!string.Equals(world.LocationId, "tarkovstreets", StringComparison.OrdinalIgnoreCase)) return false;
            switch (game.Status)
            {
                case GameStatus.Running:
                case GameStatus.Runned:
                case GameStatus.Starting:
                case GameStatus.Started:
                    break;
                default:
                    return false;
            }
            // EFT attaches the player owner after local-player creation. Fika reaches this
            // same native state before offering Start Raid; gameplay Started comes later.
            // Do not use BaseLocalGame.LocalPlayer: subclasses can keep their own player field.
            if (game is BaseLocalGame<EftGamePlayerOwner> local)
            {
                var owner = local.PlayerOwner;
                var player = owner ? owner.Player : null;
                return player && player.GameWorld == world;
            }
            return game.Status == GameStatus.Started;
        }
    }
}
