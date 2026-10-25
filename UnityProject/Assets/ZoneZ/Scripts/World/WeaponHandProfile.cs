using UnityEngine;

namespace ZoneZ
{
    internal enum HandFamily { LongGun, Pistol, DualPistols, Knife, VerticalGrip, Bullpup, Pump, ShellFed, MachineGun, Revolver }

    /// <summary>Presentation-only wrist fitting. Sockets describe palm contact in weapon space.</summary>
    internal sealed class WeaponHandProfile
    {
        public HandFamily Family;
        public bool FittedAk, Compact, SupportOnPump;
        public Vector3 TriggerPosition, SupportPosition;
        public Quaternion TriggerRotation, SupportRotation;
        public Vector3 TriggerPalm = new Vector3(0, .105f, .029f);
        public Vector3 SupportPalm = new Vector3(0, .101f, .029f);
        public Vector3 LoadPosition;
        public Quaternion LoadRotation;
        public float SupportRelease;

        public static WeaponHandProfile Create(Transform root, WeaponPresentation view, bool compact)
        {
            string id = view.WeaponId ?? "";
            bool pistol = compact && !view.Melee;
            var p = new WeaponHandProfile { Compact = compact, Family = pistol ? HandFamily.Pistol : HandFamily.LongGun };
            p.FittedAk = id == "weapon_ak47" && root.Find("Imported AK AK");
            if (id.Contains("elite")) p.Family = HandFamily.DualPistols;
            else if (view.Melee) p.Family = HandFamily.Knife;
            else if (id.Contains("revolver")) p.Family = HandFamily.Revolver;
            else if (id.Contains("mp7") || id.Contains("mp9") || id.Contains("negev") || id == "rocket_launcher") p.Family = HandFamily.VerticalGrip;
            else if (id.Contains("p90") || id.Contains("aug") || id.Contains("famas")) p.Family = HandFamily.Bullpup;
            else if (view.Pump) { p.Family = HandFamily.Pump; p.SupportOnPump = true; }
            else if (view.SingleShellReload) p.Family = HandFamily.ShellFed;
            else if (id.Contains("m249")) p.Family = HandFamily.MachineGun;

            view.TriggerGrip = root.Find("TriggerGrip");
            view.SupportGrip = root.Find("SupportGrip");
            view.ReloadGrip = root.Find("ReloadGrip");
            Quaternion triggerFrame = Frame(root, view.TriggerGrip), supportFrame = Frame(root, view.SupportGrip);
            var triggerContact = Contact(root, view.TriggerGrip, new Vector3(0, pistol ? -.073f : -.095f, pistol ? -.04f : -.057f));
            var supportContact = Contact(root, view.SupportGrip, new Vector3(0, .03f, .27f));
            // The finger row is anatomical local X. Align that row with a
            // vertical grip; local Y points from the wrist toward the knuckles.
            p.TriggerRotation = triggerFrame * Quaternion.LookRotation(Vector3.left, new Vector3(0, .30f, 1));
            p.SupportRotation = supportFrame * Quaternion.LookRotation(Vector3.up, Vector3.left);

            if (pistol)
            {
                // Keep the support palm outside the firing fingers, with the
                // knuckles running down the grip rather than standing above it.
                supportContact = triggerContact + supportFrame * new Vector3(-.012f, -.010f, .010f);
                p.SupportRotation = supportFrame * Quaternion.LookRotation(Vector3.right, new Vector3(0, .30f, 1));
            }
            if (p.Family == HandFamily.DualPistols)
            {
                supportContact = Contact(root, view.SupportGrip, triggerContact + new Vector3(-.19f, -.025f, -.045f));
                p.SupportRotation = supportFrame * Quaternion.LookRotation(Vector3.right, new Vector3(0, .30f, 1));
            }
            if (p.Family == HandFamily.VerticalGrip)
            {
                supportContact = Contact(root, view.SupportGrip, new Vector3(0, -.057f, id == "rocket_launcher" ? .31f : id.Contains("negev") ? .31f : .15f));
                p.SupportRotation = supportFrame * Quaternion.LookRotation(Vector3.right, new Vector3(0, .18f, 1));
            }
            if (p.Family == HandFamily.Bullpup && !view.SupportGrip)
                supportContact = new Vector3(0, id.Contains("p90") ? -.025f : .03f, id.Contains("p90") ? .15f : .24f);
            if (p.SupportOnPump && !view.SupportGrip)
                supportContact = root.InverseTransformPoint(view.Pump.position) + new Vector3(0, -.025f, .005f);
            if (p.Family == HandFamily.Knife)
            {
                triggerContact = Contact(root, view.TriggerGrip, new Vector3(0, 0, -.025f));
                p.TriggerRotation = triggerFrame * Quaternion.LookRotation(Vector3.left, Vector3.up);
                p.SupportRotation = Quaternion.LookRotation(new Vector3(.68f, .72f, -.12f), new Vector3(0, .20f, 1));
                supportContact = new Vector3(-.20f, .006f, .12f);
                p.SupportRelease = 65;
            }
            p.TriggerPosition = triggerContact - p.TriggerRotation * p.TriggerPalm;
            p.SupportPosition = supportContact - p.SupportRotation * p.SupportPalm;
            p.LoadRotation = Quaternion.LookRotation(Vector3.right, new Vector3(0, .30f, 1));
            p.LoadPosition = Contact(root, view.ReloadGrip, new Vector3(-.025f, -.074f, .06f));
            if (p.FittedAk)
            {
                // Preserve the already reviewed AK fit, including its curved left sleeve.
                p.TriggerPosition = new Vector3(.037f, -.045f, -.155f);
                p.TriggerRotation = Quaternion.LookRotation(Vector3.left, new Vector3(0, .30f, 1));
                p.SupportPosition = new Vector3(.015f, .026f, .21f);
                p.SupportRotation = Quaternion.LookRotation(new Vector3(.35f, .94f, 0), Vector3.forward);
            }
            return p;
        }

        static Vector3 Contact(Transform root, Transform socket, Vector3 fallback) => socket ? root.InverseTransformPoint(socket.position) : fallback;
        static Quaternion Frame(Transform root, Transform socket) => socket ? Quaternion.Inverse(root.rotation) * socket.rotation : Quaternion.identity;
    }
}
