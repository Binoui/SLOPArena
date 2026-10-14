using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client.World
{
    /// <summary>Phase 1 contact playground, separate from the Slopball challenge.</summary>
    public sealed class ArcadeBallLabMatch : ArcadeBallMatch
    {
        public const string ArenaKey = "arcade_ball_lab";
        protected override string StageKey => ArenaKey;
        protected override void DrawChallengeHUD(ArcadeBallState ball)
        {
            GUILayout.BeginArea(new Rect(18, 72, Mathf.Min(390, Screen.width - 36), 112), GUI.skin.box);
            GUILayout.Label("ARCADE BALL LAB · PHASE 1");
            GUILayout.Label($"{FighterName} · Ball {ball.DamagePercent:0}% · Contacts {AttackContacts}");
            GUILayout.Label("Normal moves / aim / orbit · Backspace: fresh ball · Esc: pause");
            GUILayout.Label("Physics playground — floors rebound.");
            GUILayout.EndArea();
        }
    }
}
