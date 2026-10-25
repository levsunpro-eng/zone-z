using UnityEngine;

namespace ZoneZ
{
    /// <summary>Weapon presentation built from adapted CC0 models and original authored fallbacks.</summary>
    public static partial class WeaponModelFactory
    {
        public static WeaponPresentation Create(Transform parent, WeaponSpec spec, SkinPreset skin, bool firstPerson)
        {
            var root = WorldArt.Group(parent, "Weapon " + spec.Id);
            var view = root.gameObject.AddComponent<WeaponPresentation>(); view.FirstPerson = firstPerson;
            skin = skin ?? new SkinPreset();
            var paint = SkinMaterial(view, "Finish A", skin.colorA, new Color(.24f, .29f, .24f), .45f);
            var grip = SkinMaterial(view, "Finish B", skin.colorB, new Color(.065f, .08f, .095f), .13f);
            var accent = SkinMaterial(view, "Finish C", skin.colorC, new Color(.57f, .46f, .26f), .6f);
            var metal = WorldArt.Mat("Weapon steel", new Color(.08f, .09f, .105f), .8f, .45f);
            var silver = WorldArt.Mat("Weapon machined steel", new Color(.46f, .48f, .47f), .85f, .6f);
            string id = spec.Id.ToLowerInvariant();
            bool pistol = spec.Slot == "pistol" || id.Contains("glock") || id.Contains("elite") || id.Contains("revolver");
            bool knife = id.Contains("knife");
            bool zeus = id.Contains("taser");
            bool shotgun = id.Contains("nova") || id.Contains("xm1014") || id.Contains("mag7") || id.Contains("sawedoff");
            bool sniper = id.Contains("awp") || id.Contains("ssg08") || id.Contains("scar20") || id.Contains("g3sg1");
            bool lmg = id.Contains("m249") || id.Contains("negev");
            bool smg = id.Contains("mp") || id.Contains("p90") || id.Contains("bizon") || id.Contains("mac10");
            view.WeaponId = id; view.Melee = knife; view.Electric = zeus;
            view.SingleShellReload = spec.SingleShellReload;

            bool imported = ImportedWeaponModels.TryBuild(root, view, id, skin, paint, grip, accent);
            if (!imported)
            {
                ApplySkinPattern(view, paint, skin);
                ImportedArsenalModels.Surface(paint,"Steel");
                ImportedArsenalModels.Surface(grip,"Polymer");
                ImportedArsenalModels.Surface(accent,"Wood");
                ImportedArsenalModels.Surface(metal,"Steel");
                ImportedArsenalModels.Surface(silver,"Steel");
                bool importedFamily=BuildFreeArsenal(root,view,id,paint,grip,accent,metal,spec.HasSilencer);
                if (importedFamily) { }
                else if (knife) BuildKnife(root, view, paint, grip, accent, silver);
                else if (zeus) BuildZeus(root, view, paint, grip, accent, silver);
                else if (pistol) BuildPistol(root, view, id, paint, grip, accent, metal, silver, spec.HasSilencer);
                else if (shotgun) BuildShotgun(root, view, id, paint, grip, accent, metal);
                else if (sniper) BuildSniper(root, view, id, paint, grip, accent, metal, silver);
                else if (lmg) BuildMachineGun(root, view, id, paint, grip, accent, metal);
                else if (smg) BuildSmg(root, view, id, paint, grip, accent, metal, spec.HasSilencer);
                else BuildRifle(root, view, id, paint, grip, accent, metal, spec.HasSilencer);
                if (!importedFamily)
                {
                    if (!knife && !zeus) AddFineDetails(root, pistol, paint, metal, accent);
                    AddIdentityDetails(root, view, id, paint, grip, accent, metal);
                    UpgradeAuthoredArsenal(root,view,id,paint,grip,accent,metal);
                }
            }
            if (skin.stickers != null) AddSkinStickers(root, skin, pistol, knife, accent);
            AddArsenalSockets(root,view,id);
            if (firstPerson) AddHands(root, view, grip, pistol || knife || zeus);
            view.Finish(); return view;
        }

        static Material SkinMaterial(WeaponPresentation owner, string name, string hex, Color fallback, float metal)
        {
            Color color = fallback;
            if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString("#" + hex.TrimStart('#'), out var parsed)) color = parsed;
            var material = WorldArt.NewMaterial(name, color, metal, .32f); owner.OwnedResources.Add(material); return material;
        }

        static Transform B(Transform root, string name, Vector3 pos, Vector3 size, Material mat, Vector3 angle = default)
        {
            var block = WorldArt.Box(root, name, pos, size, mat, false, "metal", angle);
            var actualSize = Vector3.Scale(size, root.lossyScale);
            if (Mathf.Min(actualSize.x, actualSize.y, actualSize.z) >= .025f && Mathf.Max(actualSize.x, actualSize.y, actualSize.z) >= .08f)
                block.GetComponent<MeshFilter>().sharedMesh = BeveledBlock(size);
            var sculpted=ContouredBlock(name);
            if(sculpted) block.GetComponent<MeshFilter>().sharedMesh=sculpted;
            return block.transform;
        }
        static Transform Barrel(Transform root, string name, float z, float length, float radius, float y, Material mat)
            => WorldArt.Cylinder(root, name, new Vector3(0, y, z), radius, length, mat, false, "metal", new Vector3(90, 0, 0)).transform;
        static void Muzzle(Transform root, WeaponPresentation view, float z, float y = .105f)
        { view.Muzzle = WorldArt.Group(root, "Muzzle", new Vector3(0, y, z)); view.Length = z + .35f; }
        static void Grip(Transform root, Material mat, Vector3 pos, float length = .14f, float angle = -12)
            => B(root, "Pistol grip", pos, new Vector3(.045f, length, .063f), mat, new Vector3(angle, 0, 0));
        static void Trigger(Transform root, Material metal, float z = .015f)
        {
            B(root, "Trigger guard lower", new Vector3(0, -.095f, z + .036f), new Vector3(.031f, .009f, .09f), metal);
            B(root, "Trigger guard front", new Vector3(0, -.065f, z + .076f), new Vector3(.03f, .068f, .009f), metal);
            B(root, "Trigger", new Vector3(0, -.05f, z + .025f), new Vector3(.012f, .037f, .012f), metal, new Vector3(-20, 0, 0));
        }
        static Transform Stock(Transform root, Material mat, bool skeletal = false, float length = .24f)
        {
            var holder = WorldArt.Group(root, "Stock", new Vector3(0, .025f, -.16f - length / 2));
            if (skeletal)
            {
                B(holder, "Stock buffer", new Vector3(0, .034f, 0), new Vector3(.032f, .028f, length), mat);
                B(holder, "Stock brace", new Vector3(0, -.018f, -.025f), new Vector3(.025f, .021f, length * .7f), mat, new Vector3(-12, 0, 0));
            }
            else WorldArt.Taper(holder, "Shaped buttstock", Vector3.zero, new Vector3(.059f, .14f, 0), new Vector3(.05f, .068f, 0), length, mat);
            B(holder, "Recoil pad", new Vector3(0, -.01f, -length * .5f), new Vector3(.063f, .14f, .026f), WorldArt.Mat("Rubber pads", new Color(.045f, .047f, .05f)));
            return holder;
        }
        static Transform Magazine(Transform root, Material mat, bool curved, float z = .11f, float height = .20f)
        {
            var mag = WorldArt.Group(root, "Magazine", new Vector3(0, -.085f, z));
            if (curved)
            {
                for (int i = 0; i < 4; i++) B(mag, "Curved magazine segment " + i, new Vector3(0, -.02f - height * i / 4, i * .012f), new Vector3(.043f, height / 4 + .008f, .075f), mat, new Vector3(-i * 7, 0, 0));
                for (int side = -1; side <= 1; side += 2) for (int i = 0; i < 3; i++) B(mag, "Pressed magazine rib", new Vector3(side * .023f, -.085f, .005f + i * .022f), new Vector3(.003f, height * .65f, .004f), mat, new Vector3(-9, 0, 0));
            }
            else B(mag, "Magazine body", new Vector3(0, -height * .5f, 0), new Vector3(.045f, height, .077f), mat, new Vector3(-5, 0, 0));
            return mag;
        }
        static void Sights(Transform root, Material mat, float frontZ, float y = .18f, float barrelY = .105f, float rearZ = -.10f)
        {
            float baseBottom = barrelY - .009f, baseTop = y - .016f;
            B(root, "Front sight mounting block", new Vector3(0, (baseBottom + baseTop) * .5f, frontZ), new Vector3(.027f, Mathf.Max(.015f, baseTop - baseBottom), .034f), mat);
            B(root, "Front sight post", new Vector3(0, y, frontZ), new Vector3(.012f, .055f, .019f), mat);
            B(root, "Rear sight mounting block", new Vector3(0, (barrelY + y - .009f) * .5f, rearZ), new Vector3(.047f, Mathf.Max(.018f, y - .009f - barrelY), .035f), mat);
            B(root, "Rear sight left", new Vector3(-.013f, y, rearZ), new Vector3(.012f, .025f, .023f), mat);
            B(root, "Rear sight right", new Vector3(.013f, y, rearZ), new Vector3(.012f, .025f, .023f), mat);
        }
        static void Rail(Transform root, Material mat, float start, float end, float y = .14f)
        {
            B(root, "Accessory rail", new Vector3(0, y, (start + end) * .5f), new Vector3(.035f, .009f, end - start), mat);
            int count = Mathf.Clamp(Mathf.RoundToInt((end - start) / .025f), 3, 18);
            for (int i = 0; i < count; i++) B(root, "Rail tooth", new Vector3(0, y + .007f, Mathf.Lerp(start, end, i / (float)count)), new Vector3(.049f, .007f, .013f), mat);
        }
        static void Scope(Transform root, Material metal, float length = .27f, float y = .23f)
        {
            B(root, "Scope rear mount", new Vector3(0, y - .065f, -.04f), new Vector3(.045f, .063f, .038f), metal);
            B(root, "Scope front mount", new Vector3(0, y - .065f, .11f), new Vector3(.045f, .063f, .038f), metal);
            Barrel(root, "Scope body", .06f, length, .027f, y, metal);
            Barrel(root, "Scope objective housing", .06f + length * .46f, .064f, .039f, y, metal);
            Barrel(root, "Scope eyepiece", .06f - length * .46f, .048f, .033f, y, metal);
            var glass = WorldArt.Mat("Scope lens", new Color(.075f, .24f, .29f), .65f, .92f);
            Barrel(root, "Objective blue lens", .062f + length * .56f, .002f, .032f, y, glass);
            Barrel(root, "Eye lens", .058f - length * .56f, .002f, .025f, y, glass);
            WorldArt.Cylinder(root, "Elevation turret", new Vector3(0, y + .037f, .07f), .02f, .027f, metal);
            WorldArt.Cylinder(root, "Windage turret", new Vector3(.036f, y, .07f), .017f, .027f, metal, false, "metal", new Vector3(0, 0, 90));
        }

        static void BuildRifle(Transform r, WeaponPresentation v, string id, Material a, Material b, Material c, Material steel, bool silenced)
        {
            bool ak = id.Contains("ak47"); bool famas = id.Contains("famas"); bool aug = id.Contains("aug"); bool galil = id.Contains("galil"); bool sg = id.Contains("sg556");
            bool bullpup = famas || aug;
            B(r, "Upper receiver", new Vector3(0, .075f, 0), new Vector3(.069f, .105f, bullpup ? .37f : .31f), a);
            WorldArt.Taper(r, "Receiver crown", new Vector3(0, .13f, -.015f), new Vector3(.059f, .018f, 0), new Vector3(.044f, .018f, 0), .29f, steel);
            Grip(r, b, new Vector3(0, -.10f, -.057f)); Trigger(r, steel, -.015f);
            Stock(r, bullpup ? a : b, !ak && !bullpup, bullpup ? .17f : .25f);
            float handLength = ak ? .20f : galil ? .26f : .28f;
            B(r, ak ? "Timber fore-end" : "Ventilated fore-end", new Vector3(0, .073f, .22f), new Vector3(.078f, .078f, handLength), ak ? c : a);
            Barrel(r, "Exposed barrel", .39f, .29f, .014f, .105f, steel);
            Barrel(r, "Gas tube", .24f, .21f, .011f, .135f, steel);
            float muzzle = .535f;
            if (silenced) { v.Suppressor = Barrel(r, "Suppressor", .57f, .20f, .029f, .105f, steel); v.BareMuzzleZ = .54f; muzzle = .675f; }
            else { Barrel(r, "Muzzle brake", .54f, .049f, .021f, .105f, steel); muzzle = .57f; }
            v.Magazine = Magazine(r, b, ak || galil, bullpup ? -.17f : .08f, ak ? .24f : .17f);
            if (famas)
            {
                B(r, "FAMAS handle bridge", new Vector3(0, .23f, 0), new Vector3(.045f, .035f, .28f), a);
                B(r, "Handle front", new Vector3(0, .177f, .115f), new Vector3(.043f, .075f, .024f), a);
                B(r, "Handle rear", new Vector3(0, .177f, -.115f), new Vector3(.043f, .075f, .024f), a);
            }
            else if (aug || sg) Scope(r, steel, .19f, .235f);
            else { if (!ak) Rail(r, steel, -.12f, .32f); Sights(r, steel, .41f); }
            for (int i = 0; i < 5; i++) for (int side = -1; side <= 1; side += 2)
                B(r, "Handguard vent", new Vector3(side * .0405f, .081f, .13f + i * .041f), new Vector3(.003f, .020f, .02f), steel);
            if (ak) B(r, "AK charging handle", new Vector3(.053f, .086f, .018f), new Vector3(.047f, .015f, .019f), steel);
            if (aug) B(r, "AUG forward grip", new Vector3(0, -.007f, .27f), new Vector3(.044f, .14f, .052f), b, new Vector3(12, 0, 0));
            Muzzle(r, v, muzzle);
        }

        static void BuildPistol(Transform r, WeaponPresentation v, string id, Material a, Material b, Material c, Material steel, Material silver, bool silenced)
        {
            bool revolver = id.Contains("revolver"); bool deagle = id.Contains("deagle"); bool dual = id.Contains("elite");
            float length = deagle || revolver ? .245f : .19f;
            B(r, "Pistol frame", new Vector3(0, .022f, .046f), new Vector3(.046f, .061f, length), a);
            v.Slide = B(r, "Reciprocating slide", new Vector3(0, .071f, .045f), new Vector3(deagle ? .047f : .040f, .057f, length), deagle ? c : a);
            Grip(r, b, new Vector3(0, -.075f, -.039f), .138f, -17); Trigger(r, steel, -.015f);
            v.Magazine = B(r, "Magazine base", new Vector3(0, -.143f, -.062f), new Vector3(.054f, .013f, .074f), b);
            Barrel(r, "Pistol barrel", .055f, length, .011f, .077f, steel);
            float muzzle = .06f + length * .5f;
            if (silenced) { v.BareMuzzleZ = muzzle; v.Suppressor = Barrel(r, "Pistol suppressor", muzzle + .09f, .18f, .024f, .077f, steel); muzzle += .185f; }
            for (int i = 0; i < 6; i++) for (int side = -1; side <= 1; side += 2)
                B(r, "Slide serration", new Vector3(side * .022f, .075f, -.022f + i * .008f), new Vector3(.003f, .039f, .002f), steel, new Vector3(-12, 0, 0));
            Sights(r, steel, length * .55f, .11f, .077f, -.035f);
            if (revolver)
            {
                v.Slide.gameObject.SetActive(false);
                v.Magazine.gameObject.SetActive(false); v.Magazine = Barrel(r, "Revolver cylinder", -.013f, .075f, .044f, .072f, silver);
                v.MagazineRemoval = new Vector3(-.095f, 0, 0);
                for (int i = 0; i < 6; i++)
                {
                    float angle = i * Mathf.PI / 3;
                    WorldArt.Cylinder(r, "Cylinder chamber", new Vector3(Mathf.Cos(angle) * .028f, .072f + Mathf.Sin(angle) * .028f, .026f), .009f, .004f, steel, false, "metal", new Vector3(90, 0, 0));
                }
                B(r, "Heavy revolver shroud", new Vector3(0, .058f, .14f), new Vector3(.037f, .062f, .17f), a);
                B(r, "Revolver hammer", new Vector3(0, .10f, -.064f), new Vector3(.016f, .04f, .026f), silver);
                muzzle = .23f;
            }
            if (id.Contains("tec9")) { Barrel(r, "Tec upper cylinder", .046f, .215f, .032f, .075f, a); v.Magazine = Magazine(r, b, false, .047f, .17f); }
            if (id.Contains("cz75")) B(r, "CZ front grip spare magazine", new Vector3(0, -.052f, .125f), new Vector3(.039f, .11f, .047f), b);
            if (dual)
            {
                // Distinct paired silhouette; second pistol is an authored lightweight copy, not a duplicate MonoBehaviour.
                var second = WorldArt.Group(r, "Left paired pistol", new Vector3(-.19f, -.025f, -.045f), new Vector3(0, -5, 0));
                B(second, "Left slide", new Vector3(0, .07f, .05f), new Vector3(.044f, .055f, .22f), a);
                B(second, "Left frame", new Vector3(0, .027f, .05f), new Vector3(.044f, .044f, .2f), steel);
                Grip(second, b, new Vector3(0, -.068f, -.04f)); Trigger(second, steel); Sights(second, steel, .14f, .107f, .077f, -.035f);
            }
            Muzzle(r, v, muzzle, .077f);
        }

        static void BuildSmg(Transform r, WeaponPresentation v, string id, Material a, Material b, Material c, Material steel, bool silenced)
        {
            bool p90 = id.Contains("p90"); bool bizon = id.Contains("bizon"); bool mac = id.Contains("mac10"); bool mp7 = id.Contains("mp7"); bool mp9 = id.Contains("mp9"); bool ump = id.Contains("ump");
            float bodyLength = mac ? .21f : p90 ? .39f : .28f;
            B(r, "SMG receiver", new Vector3(0, .069f, .02f), new Vector3(p90 ? .087f : .066f, p90 ? .12f : .10f, bodyLength), a);
            Grip(r, b, new Vector3(0, -.091f, -.035f)); Trigger(r, steel, -.014f);
            if (mac || mp7 || mp9) v.Magazine = Magazine(r, b, false, -.045f, mac ? .22f : .16f);
            else if (!p90 && !bizon) v.Magazine = Magazine(r, b, !ump, .09f, ump ? .21f : .16f);
            if (p90)
            {
                B(r, "P90 bullpup stock", new Vector3(0, -.005f, -.18f), new Vector3(.079f, .19f, .15f), b);
                v.Magazine = WorldArt.Group(r, "P90 magazine rig", new Vector3(0, .146f, .034f));
                B(v.Magazine, "P90 horizontal magazine", Vector3.zero, new Vector3(.067f, .025f, .35f), c);
                v.MagazineRemoval = new Vector3(0, .20f, -.10f);
                for (int i = 0; i < 9; i++) B(v.Magazine, "P90 cartridge stripe", new Vector3(0, .014f, -.109f + i * .027f), new Vector3(.051f, .003f, .008f), steel);
                B(r, "P90 foregrip", new Vector3(0, -.013f, .166f), new Vector3(.07f, .15f, .061f), b, new Vector3(-25, 0, 0));
            }
            else if (!mac) Stock(r, b, true, .16f);
            else { B(r, "Wire stock left", new Vector3(-.037f, .044f, -.20f), new Vector3(.009f, .009f, .23f), steel); B(r, "Wire stock right", new Vector3(.037f, .044f, -.20f), new Vector3(.009f, .009f, .23f), steel); B(r, "Wire butt", new Vector3(0, .002f, -.31f), new Vector3(.084f, .09f, .012f), steel); }
            if (bizon) { v.Magazine = Barrel(r, "Helical magazine", .17f, .33f, .04f, -.01f, b); for (int i = 0; i < 8; i++) Barrel(r, "Helical magazine groove", .025f + i * .043f, .003f, .041f, -.01f, steel); }
            float muzzle = mac ? .235f : .355f;
            Barrel(r, "SMG barrel", mac ? .185f : .25f, mac ? .09f : .20f, .014f, .105f, steel);
            if (silenced || id.Contains("mp5sd")) { Barrel(r, "Integral silencer", .35f, .24f, .032f, .105f, steel); muzzle = .477f; }
            if (mp7 || mp9) B(r, "Vertical forward grip", new Vector3(0, -.028f, .15f), new Vector3(.039f, .16f, .049f), b);
            Rail(r, steel, -.095f, .12f, p90 ? .2f : .136f); Sights(r, steel, .19f, p90 ? .24f : .17f);
            Muzzle(r, v, muzzle);
        }

        static void BuildShotgun(Transform r, WeaponPresentation v, string id, Material a, Material b, Material c, Material steel)
        {
            bool shortGun = id.Contains("sawedoff"); bool mag7 = id.Contains("mag7"); bool auto = id.Contains("xm1014");
            B(r, "Shotgun action", new Vector3(0, .064f, -.005f), new Vector3(.073f, .094f, .24f), a);
            float muzzle = shortGun ? .32f : mag7 ? .34f : .66f;
            Barrel(r, "Shotgun bore", (muzzle + .03f) * .5f, muzzle - .03f, .023f, .10f, steel);
            Barrel(r, "Shell tube", (muzzle - .015f) * .5f, muzzle - .085f, .017f, .045f, steel);
            if (shortGun) Grip(r, c, new Vector3(0, -.062f, -.075f), .13f, -34);
            else { Stock(r, auto ? b : c, auto, .25f); Grip(r, b, new Vector3(0, -.09f, -.066f)); }
            Trigger(r, steel, -.035f);
            if (mag7)
            {
                v.Magazine = Magazine(r, b, false, .059f, .14f);
                B(r, "Box receiver upper", new Vector3(0, .117f, .066f), new Vector3(.082f, .045f, .30f), a);
            }
            else
            {
                float pumpZ = shortGun ? .15f : .28f;
                var pump = B(r, auto ? "Shotgun handguard" : "Pump fore-end", new Vector3(0, .015f, pumpZ), new Vector3(.073f, .064f, .20f), auto ? b : c);
                if (!auto) v.Pump = pump;
                for (int i = 0; i < 9; i++) B(pump, "Pump rib", new Vector3(0, 0, -.435f + i * .105f), new Vector3(1.065f, 1.046f, .03f), b);
            }
            for (int i = 0; i < 4; i++) WorldArt.Cylinder(r, "Receiver shell holder", new Vector3(-.047f, .064f, -.055f + i * .036f), .011f, .065f, c);
            Sights(r, steel, muzzle - .035f, .135f, .10f); Muzzle(r, v, muzzle, .1f);
        }

        static void BuildSniper(Transform r, WeaponPresentation v, string id, Material a, Material b, Material c, Material steel, Material silver)
        {
            bool awp = id.Contains("awp"); bool scout = id.Contains("ssg08"); bool scar = id.Contains("scar20");
            float muzzle = awp ? .78f : scout ? .67f : .64f;
            B(r, "Precision receiver", new Vector3(0, .075f, .01f), new Vector3(.069f, .078f, .37f), steel);
            WorldArt.Taper(r, "Sniper chassis", new Vector3(0, .005f, .11f), new Vector3(.09f, .11f, 0), new Vector3(.065f, .075f, 0), .50f, a);
            Stock(r, a, scout || scar, awp ? .29f : .24f); Grip(r, b, new Vector3(0, -.088f, -.083f), .15f); Trigger(r, steel, -.055f);
            v.Magazine = Magazine(r, b, false, .055f, awp || scout ? .08f : .15f);
            Barrel(r, "Heavy precision barrel", (muzzle + .06f) * .5f, muzzle - .06f, awp ? .02f : .016f, .105f, steel);
            Barrel(r, "Barrel end", muzzle - .017f, .05f, .025f, .105f, steel);
            Scope(r, steel, awp ? .34f : .29f, .25f);
            if (awp || scout)
            {
                WorldArt.Beam(r, "Bolt lever", new Vector3(.029f, .096f, -.069f), new Vector3(.10f, .037f, -.062f), .015f, silver);
                WorldArt.Sphere(r, "Bolt knob", new Vector3(.10f, .037f, -.062f), Vector3.one * .031f, b);
            }
            else Rail(r, steel, .20f, .36f);
            if (awp || scar)
            {
                WorldArt.Beam(r, "Folded bipod left", new Vector3(-.03f, .015f, .31f), new Vector3(-.057f, -.037f, .50f), .013f, steel);
                WorldArt.Beam(r, "Folded bipod right", new Vector3(.03f, .015f, .31f), new Vector3(.057f, -.037f, .50f), .013f, steel);
            }
            if (scar) B(r, "SCAR adjustable cheek rest", new Vector3(0, .095f, -.33f), new Vector3(.065f, .05f, .17f), c);
            Muzzle(r, v, muzzle);
        }

        static void BuildMachineGun(Transform r, WeaponPresentation v, string id, Material a, Material b, Material c, Material steel)
        {
            bool negev = id.Contains("negev");
            B(r, "Machine gun receiver", new Vector3(0, .075f, .016f), new Vector3(.11f, .14f, .32f), a);
            B(r, "Feed cover", new Vector3(0, .153f, .013f), new Vector3(.106f, .022f, .27f), b);
            Grip(r, b, new Vector3(0, -.105f, -.09f)); Trigger(r, steel, -.04f); Stock(r, b, negev, .25f);
            v.Magazine = B(r, "Belt ammunition box", new Vector3(-.02f, -.11f, .095f), new Vector3(.14f, .18f, .16f), negev ? c : b);
            Barrel(r, "Machine gun barrel", .45f, .60f, .021f, .105f, steel);
            B(r, "Perforated handguard", new Vector3(0, .07f, .28f), new Vector3(.096f, .084f, .27f), a);
            for (int i = 0; i < 7; i++) for (int side = -1; side <= 1; side += 2)
                B(r, "Cooling vent", new Vector3(side * .049f, .078f, .166f + i * .037f), new Vector3(.003f, .03f, .02f), steel);
            for (int i = 0; i < 7; i++) WorldArt.Cylinder(r, "Visible ammunition belt", new Vector3(-.04f - i * .015f, .09f - i * .009f, .021f), .006f, .06f, c, false, "metal", new Vector3(90, 0, 0));
            B(r, "Carry handle", new Vector3(.03f, .224f, .15f), new Vector3(.03f, .024f, .18f), b);
            B(r, "Carry handle bracket", new Vector3(.03f, .173f, .075f), new Vector3(.025f, .11f, .024f), steel);
            WorldArt.Beam(r, "Bipod left", new Vector3(-.035f, .05f, .48f), new Vector3(-.07f, -.03f, .69f), .017f, steel);
            WorldArt.Beam(r, "Bipod right", new Vector3(.035f, .05f, .48f), new Vector3(.07f, -.03f, .69f), .017f, steel);
            Rail(r, steel, -.10f, .12f, .177f); Sights(r, steel, .65f, .175f); Muzzle(r, v, .76f);
        }

        static void BuildKnife(Transform r, WeaponPresentation v, Material a, Material b, Material c, Material steel)
        {
            B(r, "Knife handle", new Vector3(0, 0, -.025f), new Vector3(.035f, .034f, .14f), b);
            for (int i = 0; i < 6; i++) B(r, "Grip rib", new Vector3(0, 0, -.082f + .021f * i), new Vector3(.039f, .039f, .005f), a);
            B(r, "Crossguard", new Vector3(0, 0, .05f), new Vector3(.018f, .095f, .019f), c);
            WorldArt.Taper(r, "Blade", new Vector3(0, 0, .168f), new Vector3(.007f, .052f, 0), new Vector3(.002f, .001f, 0), .22f, steel);
            B(r, "Blade fuller", new Vector3(.0041f, 0, .13f), new Vector3(.001f, .008f, .11f), a);
            Muzzle(r, v, .282f, 0);
        }
        static void BuildZeus(Transform r, WeaponPresentation v, Material a, Material b, Material c, Material steel)
        {
            B(r, "Zeus housing", new Vector3(0, .039f, .032f), new Vector3(.055f, .10f, .15f), a); Grip(r, b, new Vector3(0, -.071f, -.019f), .12f, -10);
            B(r, "Cartridge socket", new Vector3(0, .046f, .119f), new Vector3(.064f, .075f, .043f), b);
            B(r, "Zeus cartridge safety band", new Vector3(0, .046f, .101f), new Vector3(.066f, .078f, .012f), c);
            for (int side = -1; side <= 1; side += 2) B(r, "Electrode", new Vector3(side * .024f, .061f, .15f), new Vector3(.008f, .012f, .033f), steel);
            var blue = WorldArt.Mat("Zeus indicator", new Color(.25f, .76f, 1), .1f, .5f);
            B(r, "Charge indicator", new Vector3(.03f, .061f, .04f), new Vector3(.004f, .012f, .035f), blue); Trigger(r, steel, 0); Muzzle(r, v, .168f, .061f);
        }
        static void AddFineDetails(Transform r, bool pistol, Material a, Material steel, Material accent)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < 3; i++) WorldArt.Cylinder(r, "Receiver pin", new Vector3(side * (pistol ? .024f : .037f), .044f, -.055f + i * .038f), .005f, .002f, accent, false, "metal", new Vector3(0, 0, 90));
                B(r, "Selector", new Vector3(side * (pistol ? .026f : .039f), .068f, -.051f), new Vector3(.005f, .007f, .024f), steel, new Vector3(-20, 0, 0));
            }
            B(r, "Ejection port", new Vector3(pistol ? .023f : .037f, .093f, .038f), new Vector3(.002f, .020f, .042f), steel);
        }
        static void AddHands(Transform r, WeaponPresentation view, Material glove, bool compact)
        {
            if (ImportedHands.Attach(r, view, compact)) return;
            var fabric = WorldArt.Mat("Player sleeves", new Color(.19f, .23f, .19f));
            view.TriggerHand = WorldArt.Group(r, "Articulated trigger hand", new Vector3(.022f, -.074f, -.05f));
            WorldArt.Sphere(view.TriggerHand, "Right glove", Vector3.zero, new Vector3(.075f, .10f, .078f), glove);
            WorldArt.Beam(view.TriggerHand, "Right sleeve", new Vector3(.005f, -.046f, -.02f), new Vector3(.108f, -.156f, -.19f), .095f, fabric);
            for(int i=0;i<4;i++) WorldArt.Sphere(view.TriggerHand,"Gloved finger",new Vector3(-.028f,-.031f+i*.018f,.029f),new Vector3(.050f,.017f,.034f),glove);
            Vector3 support = compact ? new Vector3(-.065f,-.06f,-.062f) : new Vector3(-.025f,0,.27f);
            if(view.Melee) support = new Vector3(-.29f,-.025f,.05f);
            if(view.WeaponId == "weapon_elite") support = new Vector3(-.19f,-.099f,-.095f);
            view.SupportHand=WorldArt.Group(r,"Articulated support hand",support);
            WorldArt.Sphere(view.SupportHand,"Support glove",Vector3.zero,new Vector3(.08f,.074f,.087f),glove);
            WorldArt.Beam(view.SupportHand,"Support sleeve",new Vector3(-.024f,-.022f,-.02f),new Vector3(-.18f,-.16f,-.30f),.092f,fabric);
            for(int i=0;i<4;i++) WorldArt.Sphere(view.SupportHand,"Support finger",new Vector3(.012f,.025f,-.032f+i*.02f),new Vector3(.055f,.025f,.018f),glove);
        }
    }
}
