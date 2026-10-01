using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ScreenPortal.Domain;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.App.Session
{
    /// <summary>
    /// Última sessão em texto (uma linha por item, cultura invariante, floats "R" = ida e volta exata). Formato
    /// versionado; qualquer erro rejeita o arquivo inteiro (o app abre vazio, como antes). Fora do caminho quente.
    /// <code>
    /// screenportal-session 1
    /// surface &lt;modo&gt; &lt;cx&gt; &lt;cy&gt; &lt;cz&gt; &lt;yaw&gt; &lt;tilt&gt; &lt;ímã 0|1&gt; &lt;grade 0|1&gt; &lt;passo&gt;
    /// focus &lt;id&gt;
    /// next &lt;id&gt;
    /// screen &lt;id&gt; &lt;w&gt; &lt;h&gt; &lt;solta 0|1&gt; &lt;az&gt; &lt;el&gt; &lt;dist&gt; &lt;px&gt; &lt;py&gt; &lt;pz&gt; &lt;qx&gt; &lt;qy&gt; &lt;qz&gt; &lt;qw&gt;
    ///        &lt;largura&gt; &lt;curv&gt; &lt;últimaCurv&gt; &lt;fps tipo&gt; &lt;fps fixo&gt; &lt;origem&gt; &lt;monitor físico&gt; &lt;rótulo…&gt;
    /// </code>
    /// </summary>
    public static class SessionCodec
    {
        public const string Header = "screenportal-session 1";
        private const int ScreenFields = 21; // sem contar "screen" e o rótulo

        public static string Encode(Workspace ws)
        {
            var sb = new StringBuilder();
            sb.Append(Header).Append('\n');
            var s = ws.Surface;
            sb.Append("surface ").Append((int)s.Mode).Append(' ').Append(F(s.Center.X)).Append(' ').Append(F(s.Center.Y)).Append(' ')
                .Append(F(s.Center.Z)).Append(' ').Append(F(s.HeadingDeg)).Append(' ').Append(F(s.TiltDeg)).Append(' ')
                .Append(s.Snap.NeighborSnap ? 1 : 0).Append(' ').Append(s.Snap.AngularGrid ? 1 : 0).Append(' ')
                .Append(F(s.Snap.GridStepDeg)).Append('\n');
            sb.Append("focus ").Append(ws.FocusedScreenId.Value).Append('\n');
            sb.Append("next ").Append(ws.NextScreenId).Append('\n');
            foreach (var screen in ws.Screens)
            {
                var p = screen.Placement;
                var pose = p.LoosePose;
                sb.Append("screen ").Append(screen.Id.Value).Append(' ').Append(screen.Resolution.Width).Append(' ')
                    .Append(screen.Resolution.Height).Append(' ').Append(p.IsLoose ? 1 : 0).Append(' ')
                    .Append(F(p.AzimuthDeg)).Append(' ').Append(F(p.ElevationDeg)).Append(' ').Append(F(p.DistanceM)).Append(' ')
                    .Append(F(pose.Position.X)).Append(' ').Append(F(pose.Position.Y)).Append(' ').Append(F(pose.Position.Z)).Append(' ')
                    .Append(F(pose.Rotation.X)).Append(' ').Append(F(pose.Rotation.Y)).Append(' ').Append(F(pose.Rotation.Z)).Append(' ')
                    .Append(F(pose.Rotation.W)).Append(' ').Append(F(screen.WidthMeters)).Append(' ').Append(F(screen.Curvature)).Append(' ')
                    .Append(F(screen.LastNonZeroCurvature)).Append(' ').Append((int)screen.FrameRateMode.Kind).Append(' ')
                    .Append(screen.FrameRateMode.FixedFps).Append(' ').Append((int)screen.Source.Kind).Append(' ')
                    .Append(screen.Source.PhysicalMonitorId).Append(' ').Append(Escape(screen.Label)).Append('\n');
            }
            return sb.ToString();
        }

        public static bool TryDecode(string text, out Workspace workspace)
        {
            workspace = null;
            if (string.IsNullOrEmpty(text)) return false;
            try
            {
                var lines = text.Split('\n');
                if (lines[0].TrimEnd('\r') != Header) return false;
                var surface = LayoutSurface.Default;
                var focus = ScreenId.None;
                int next = 1;
                var screens = new List<VirtualScreen>();
                for (int i = 1; i < lines.Length; i++)
                {
                    string line = lines[i].TrimEnd('\r');
                    if (line.Length == 0) continue;
                    if (line.StartsWith("screen ", StringComparison.Ordinal)) screens.Add(DecodeScreen(line));
                    else if (line.StartsWith("surface ", StringComparison.Ordinal)) surface = DecodeSurface(line);
                    else if (line.StartsWith("focus ", StringComparison.Ordinal)) focus = new ScreenId(I(line.Substring(6)));
                    else if (line.StartsWith("next ", StringComparison.Ordinal)) next = I(line.Substring(5));
                    else return false;
                }
                workspace = Workspace.Restore(screens, focus, next, surface);
                return true;
            }
            catch (Exception e) when (e is FormatException || e is OverflowException || e is ArgumentException || e is IndexOutOfRangeException)
            {
                return false;
            }
        }

        private static LayoutSurface DecodeSurface(string line)
        {
            var t = line.Split(' ');
            if (t.Length != 10) throw new FormatException("surface");
            var snap = new SnapOptions(t[7] == "1", t[8] == "1", Fl(t[9]));
            return new LayoutSurface((LayoutMode)I(t[1]), new Vec3(Fl(t[2]), Fl(t[3]), Fl(t[4])), Fl(t[5]), Fl(t[6]), snap);
        }

        private static VirtualScreen DecodeScreen(string line)
        {
            var t = line.Split(new[] { ' ' }, ScreenFields + 2);
            if (t.Length != ScreenFields + 2) throw new FormatException("screen");
            var placement = t[4] == "1"
                ? Placement.Loose(new Pose(new Vec3(Fl(t[8]), Fl(t[9]), Fl(t[10])), new Quat(Fl(t[11]), Fl(t[12]), Fl(t[13]), Fl(t[14]))))
                : Placement.OnSurface(Fl(t[5]), Fl(t[6]), Fl(t[7]));
            var fpsKind = (FrameRateKind)I(t[18]);
            var fps = fpsKind == FrameRateKind.Fixed ? FrameRateMode.Fixed(I(t[19]))
                : fpsKind == FrameRateKind.Video ? FrameRateMode.Video
                : fpsKind == FrameRateKind.Eco ? FrameRateMode.Eco
                : FrameRateMode.Auto;
            var sourceKind = (ScreenSourceKind)I(t[20]);
            var source = sourceKind == ScreenSourceKind.Mirror ? ScreenSource.Mirror(uint.Parse(t[21], CultureInfo.InvariantCulture))
                : sourceKind == ScreenSourceKind.Test ? ScreenSource.Test
                : ScreenSource.Virtual;
            return new VirtualScreen(new ScreenId(I(t[1])), Resolution.Create(I(t[2]), I(t[3])), placement, Fl(t[15]),
                Fl(t[16]), Fl(t[17]), fps, Unescape(t[22]), source);
        }

        private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
        private static float Fl(string text) => float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        private static int I(string text) => int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);

        private static string Escape(string text) =>
            (text ?? string.Empty).Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\r", "\\r");

        private static string Unescape(string text)
        {
            var sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c != '\\' || i + 1 >= text.Length)
                {
                    sb.Append(c);
                    continue;
                }
                char n = text[++i];
                sb.Append(n == 'n' ? '\n' : n == 'r' ? '\r' : n);
            }
            return sb.ToString();
        }
    }
}
