using System.Collections.Generic;
using UnityEngine;

namespace ZoneZ
{
    public enum MinimapMarkerKind { Self, Ally, Enemy }
    public readonly struct MinimapMarker
    {
        public readonly Actor Actor;
        public readonly MinimapMarkerKind Kind;
        public readonly Vector3 Position;
        public MinimapMarker(Actor actor, MinimapMarkerKind kind) { Actor = actor; Kind = kind; Position = actor.transform.position; }
    }

    public sealed partial class GameUI
    {
        MinimapAtlas minimapAtlas;
        ZooWorld minimapWorld;
        int minimapFrame = -1;
        readonly List<MinimapMarker> minimapMarkers = new List<MinimapMarker>(24);
        public IReadOnlyList<MinimapMarker> MinimapMarkers { get { RefreshMinimap(); return minimapMarkers; } }
        public Texture2D MinimapTexture { get { RefreshMinimap(); return minimapAtlas?.Texture; } }
        public static Vector2 WorldToMinimap(Vector3 position) => MinimapAtlas.NormalizedPoint(position);
        public static Rect MinimapPanelRect(float width, float verticalOffset = 0) => new Rect(width - 268, 20 - verticalOffset, 240, 252);
        Rect CurrentMinimapPanel()
        {
            float scale = Mathf.Min(Screen.width / 1600f, Screen.height / 900f);
            return MinimapPanelRect(uiW, (Screen.height - 900 * scale) / (2 * scale));
        }

        public void RefreshMinimap(bool force = false)
        {
            if (!root || !root.World || !root.Match) return;
            if (minimapWorld != root.World || minimapAtlas == null)
            {
                if (minimapAtlas != null && minimapAtlas.Texture) Destroy(minimapAtlas.Texture);
                minimapWorld = root.World; minimapAtlas = new MinimapAtlas(root.World); force = true;
            }
            if (!force && minimapFrame == Time.frameCount) return;
            minimapFrame = Time.frameCount; minimapMarkers.Clear(); minimapAtlas.BeginFrame();
            var local = root.Match.Local;
            if (local)
            {
                var observer = local.Alive ? local : root.Match.IsTeamMode ? local.GetComponent<PlayerMotor>()?.SpectatingActor : null;
                var camera = local.ViewCamera;
                foreach (var candidate in root.Match.Actors)
                {
                    if (!candidate || !candidate.Alive || !candidate.gameObject.activeInHierarchy || candidate == observer) continue;
                    bool ally = root.Match.IsTeamMode && candidate.Team == local.Team;
                    if (ally) minimapMarkers.Add(new MinimapMarker(candidate, MinimapMarkerKind.Ally));
                    else if (root.Match.Phase == MatchPhase.Playing && MinimapVisibility.CanShow(observer, candidate, camera))
                        minimapMarkers.Add(new MinimapMarker(candidate, MinimapMarkerKind.Enemy));
                }
                foreach (var marker in minimapMarkers) minimapAtlas.Marker(marker.Position, marker.Kind, 0);
                if (observer && observer.Alive)
                {
                    var marker = new MinimapMarker(observer, MinimapMarkerKind.Self); minimapMarkers.Add(marker);
                    minimapAtlas.Marker(marker.Position, marker.Kind, camera ? camera.transform.eulerAngles.y : observer.transform.eulerAngles.y);
                }
            }
            minimapAtlas.Apply();
        }

        void Minimap()
        {
            var panelRect = CurrentMinimapPanel();
            Fill(panelRect, new Color(.025f, .045f, .05f, .94f));
            Fill(new Rect(panelRect.x, panelRect.y, panelRect.width, 2), muted * .65f);
            Text(new Rect(panelRect.x + 10, panelRect.y + 6, 180, 20), "ЗООПАРК", small, muted);
            Text(new Rect(panelRect.xMax - 27, panelRect.y + 6, 18, 20), "N", small, gold);
            if (Event.current.type == EventType.Repaint) RefreshMinimap();
            if (minimapAtlas != null)
            {
                var previous = GUI.color; GUI.color = Color.white;
                GUI.DrawTexture(new Rect(panelRect.x + 8, panelRect.y + 30, 224, 196), minimapAtlas.Texture, ScaleMode.StretchToFill, false);
                GUI.color = previous;
            }
            float legendY = panelRect.y + 232;
            Fill(new Rect(panelRect.x + 10, legendY + 4, 5, 5), gold);
            Text(new Rect(panelRect.x + 20, legendY, 36, 18), "Вы", small, muted);
            Fill(new Rect(panelRect.x + 60, legendY + 4, 5, 5), new Color(.31f, .75f, .97f));
            Text(new Rect(panelRect.x + 70, legendY, 92, 18), "Союзник", small, muted);
            Fill(new Rect(panelRect.x + 171, legendY + 4, 5, 5), new Color(1, .36f, .34f));
            Text(new Rect(panelRect.x + 181, legendY, 50, 18), "Враг", small, muted);
        }
    }
}
