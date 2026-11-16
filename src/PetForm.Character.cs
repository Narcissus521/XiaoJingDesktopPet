using System;
using System.Drawing;
using System.IO;

namespace XiaoJingDesktopPet
{
    public sealed partial class PetForm
    {
        private CharacterActionPlayer characterPlayer;
        private bool EnsureCharacterPlayer()
        {
            string outfit = ActionOutfitKey();
            if (characterPlayer != null && characterPlayer.Outfit == outfit) return true;
            ReleaseCharacterPlayer();
            try { characterPlayer = new CharacterActionPlayer(Path.Combine(actionAssetsRoot, "..", "character-actions"), outfit); return true; }
            catch (Exception error) { assetWarning = "新角色动作未能载入：" + error.Message; return false; }
        }
        private Image CharacterFrame(long now)
        {
            if (!EnsureCharacterPlayer()) return null;
            if(FeedingAt(now))return characterPlayer.FeedingFrame(now-feedStarted);
            int pose = ActionPoseIndex(now);
            bool independentScene = (SceneAction || SippingAt(now)) && !InteractionBlocksWork(now) && !FeedingAt(now);
            // Independent scenes paint their own complete actor. Keep the
            // general animator neutral underneath, so returning cannot reveal
            // an old held, seated or boat pose for one frame.
            if (ClosingLaptopAt(now) || AttentionActive || returnFlying || homeWalking || WanderVisual || FlightLandingAt(now) || independentScene || IsWorkVisual(now)) pose = 8;
            int wind = pose < 0 && activeHoliday == null && CurrentCondition() == "wind" ? CurrentSeverity() : 0;
            return characterPlayer.Frame(pose, now, lifted, wind);
        }
        private void ReleaseCharacterPlayer()
        {
            if (characterPlayer != null) { characterPlayer.Dispose(); characterPlayer = null; }
        }
    }
}
