using System;
using System.Drawing;
using System.Windows.Forms;
namespace XiaoJingDesktopPet
{
    public sealed partial class PetForm
    {
        private int wanderPhase;
        private long wanderPhaseStarted;
        private double wanderStartX, wanderLength, wanderCycles;
        private bool WanderVisual { get { return wanderPhase > 0 && wanderRemaining > 0 && activityBusy != true; } }

        private void UpdateWandering(long now, double elapsed)
        {
            bool permitted = wanderEnabled && activityBusy != true && !leftPressed && String.IsNullOrEmpty(leisure)
                && !InteractionBlocksWork(now) && !FeedingAt(now) && !petMenu.Visible;
            if (!permitted || wanderPhase > 0 && wanderRemaining <= 0) {
                if (wanderPhase > 0) { wanderPhase = 0; wanderRemaining = 0; walkUntil = 0; nextWanderAt = now + 20000; }
                return;
            }
            double scale = sizePercent / 100.0;
            if (wanderPhase == 0) {
                if (now < nextWanderAt) return;
                Rectangle area = Screen.FromRectangle(Bounds).WorkingArea;
                walkDirection = random.Next(2) == 0 ? -1 : 1;
                double room = walkDirection < 0 ? Left - area.Left : area.Right - Width - Left;
                if (room < 12 * scale) { walkDirection = -walkDirection; room = walkDirection < 0 ? Left - area.Left : area.Right - Width - Left; }
                if (room < 2 || !PrepareSideWalkArtwork(walkDirection > 0)) { nextWanderAt = now + 20000; return; }
                wanderLength = Math.Min(room, (45 + random.Next(70)) * scale);
                wanderRemaining = wanderLength; wanderStartX = wanderPositionX = Left;
                wanderCycles = Math.Max(1, Math.Ceiling(wanderLength / (96 * scale)));
                wanderPhase = 1; wanderPhaseStarted = now;
            }
            walkUntil = now + 100;
            if (wanderPhase == 1) {
                if (now - wanderPhaseStarted >= WalkTurnMilliseconds + WalkSettleMilliseconds) { wanderPhase = 2; wanderPhaseStarted = now; }
                return;
            }
            if (wanderPhase == 3) {
                if (now - wanderPhaseStarted >= WalkSettleMilliseconds + WalkTurnMilliseconds) {
                    wanderPhase = 0; wanderRemaining = 0; walkUntil = 0;
                    nextWanderAt = now + 20000 + random.Next(15000); SaveSettings();
                }
                return;
            }
            double traveled = Math.Abs(wanderPositionX - wanderStartX);
            double left = Math.Max(0, wanderLength - traveled);
            double accelerate = Math.Min(1, Math.Max(0, (now - wanderPhaseStarted) / 300.0));
            double speed = Math.Min(60 * scale * accelerate, Math.Sqrt(2 * 250 * scale * left));
            double step = Math.Min(left, Math.Max(0, Math.Min(.1, elapsed)) * speed);
            wanderPositionX += step * walkDirection;
            Location = new Point((int)Math.Round(wanderPositionX), Top);
            wanderRemaining = Math.Max(.001, wanderLength - Math.Abs(wanderPositionX - wanderStartX));
            if (wanderRemaining <= .5) {
                wanderPositionX = wanderStartX + wanderLength * walkDirection;
                Left = (int)Math.Round(wanderPositionX); wanderRemaining = .001;
                wanderPhase = 3; wanderPhaseStarted = now;
            }
        }
        private void DrawWandering(Graphics g, long now)
        {
            if (!EnsureSideWalkArtwork(sideWalkOutfit, sideWalkFacingRight)) return;
            double elapsed = now - wanderPhaseStarted;
            double stride = wanderLength / Math.Max(1, wanderCycles) / (sizePercent / 100.0);
            if (wanderPhase == 1) {
                if (elapsed < WalkTurnMilliseconds) DrawWalkTurn(g, 0, elapsed / WalkTurnMilliseconds);
                else sideWalkingRig.Draw(g, 0, stride, 1, sideWalkFacingRight);
            } else if (wanderPhase == 2) {
                double cycle = Math.Abs(wanderPositionX - wanderStartX) / Math.Max(1, wanderLength) * wanderCycles;
                sideWalkingRig.Draw(g, cycle, stride, 1, sideWalkFacingRight);
            } else if (elapsed < WalkSettleMilliseconds) sideWalkingRig.Draw(g, 0, stride, 1, sideWalkFacingRight);
            else DrawWalkTurn(g, 1, (elapsed - WalkSettleMilliseconds) / WalkTurnMilliseconds);
        }
    }
}
