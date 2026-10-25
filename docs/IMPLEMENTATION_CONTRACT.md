# Контракт реализации 2026-10-03

Этот файл координирует код первой версии. Нормативный баланс остаётся в остальных документах и data/. Реализация разрешена пользователем 03.10.2026. Namespace `ZoneZ`; проект `UnityProject`; код `Assets/ZoneZ/Scripts`.

## Владельцы

- root: GameRoot, MatchDirector, AbilityController, LootPickup, Editor build/tests, package/settings, инструменты установки и интеграция.
- combat agent: Actor, WeaponController, BotBrain, PlayerMotor, HitZone, Projectiles, AudioFX. Папка Scripts/Combat.
- UI/data agent: GameCatalog, WeaponSpec, AbilitySpec, ProfileStore, GameProfile, SkinPreset, GameUI. Папки Scripts/Data и Scripts/UI.
- art agent: ZooWorld, ActorVisual, WeaponModelFactory, WeaponPresentation, процедурные материалы, живность. Папка Scripts/World.

## Общие имена/API

### GameRoot (root)
`public static GameRoot Instance; public GameCatalog Catalog; public ProfileStore Store; public MatchDirector Match; public GameUI UI; public ZooWorld World; public bool Paused; public GameProfile Profile => Store.Current; public void SetPaused(bool); public void Notify(string);`
Awake загружает каталог/профиль, строит мир, создает MatchDirector и UI. Одно главное меню, мир остается его фоном. UI управляет курсором через GameRoot.SetPaused и Match phase.

### GameCatalog + Profile (data/UI agent)
`GameCatalog.Load()` static -> GameCatalog. `List<WeaponSpec> Weapons; List<AbilitySpec> Abilities; WeaponSpec Weapon(string id); AbilitySpec Ability(string id);`
`WeaponSpec`: public string Id,Name,Category,Slot; public float Damage,FireInterval,Range,RangeModifier,ArmorRatio,HeadMultiplier,MoveSpeed,ReloadSeconds,Recoil,Spread,StandInaccuracy,MoveInaccuracy; public int Pellets,Magazine,Reserve,RecoilSeed; public bool Automatic,HasScope,HasBurst,HasSilencer,SingleShellReload; public JObject Source,Overrides; `float Value(string field,bool alt=false,float fallback=0)`.
`AbilitySpec`: public string Id,Name; public int Cost,KillsRequired; public float Duration,Damage,Radius; public int Shots; public JObject Effect.
Каталог читает Resources/Balance/*.json через Newtonsoft.Json (пакет root установит). Сохранять чтение всех raw sourceFields для alternative modes. WeaponSpec.Slot канон primary,pistol,knife,zeus. Метры =sourceunits*0.01905.
`GameProfile`: schemaVersion=1; public int credits=500, healthLevel,speedLevel,strengthLevel; public string primaryId="weapon_ak47",pistolId="weapon_glock",ultimateId="frag_grenade"; public List<string> ownedAbilities,ownedCosmetics,rewardedMatches; public List<SkinPreset> skins; настройки sensitivity=1f, volume=.7f,fov=60f.
`SkinPreset`: public string weaponId; public string colorA="4B5446",colorB="20272C",colorC="BFA470"; public int pattern; public int[] stickers=new int[4]; public float wear; цвета HEXбез#. Используется нейтральный default при отсутствиипресета.
`ProfileStore`: public GameProfile Current; `static ProfileStore Load(); bool Save(); bool PurchaseAbility(string id,out string error); bool Upgrade(string branch,out string error); bool PurchaseCosmetic(string id,int cost,out string error); bool GrantReward(string matchId,int amount); SkinPreset SkinFor(string weaponId);`
Позже методы можно добавить, не ломая эти. Saveатомарная+backup, покупки validate/rewardidempotent. UIOnGUI допустим, но аккуратныйвизуальныйдизайн и русскийтекст.

### MatchDirector (root)
`enum MatchPhase { Menu,Preparation,Playing,RoundEnd,Results }`
`public MatchPhase Phase; public string ModeId; public bool IsTeamMode; public List<Actor> Actors; public Actor Local; public float TimeRemaining,PreparationRemaining,MatchTime; public int RoundNumber; public int[] RoundWins; public string WinnerText; public int EarnedCredits; public string MatchId;`
`StartMatch(string modeId); ReturnToMenu(); OnDeath(Actor victim,Actor killer,bool ultimate); bool IsEnemy(Actor a,Actor b); List<Actor> Standings();`
Actor обновляет HPdeathstate, MatchDirector.OnDeath обновляет статистику/серии/респавн/конец. Не дублировать статистику внутри Actor. MatchDirector обновляет MatchTime толькоPlaying. RoundEndвремязадержкивroot.

### Actor / Combat (combat agent)
`Actor : MonoBehaviour`: public int Id,Team,Kills,Deaths,Assists,Score; public string DisplayName; public bool Human,Alive; public float HP,MaxHP,Armor,SpeedMultiplier=1,DamageMultiplier=1,SpawnProtectedUntil; public Transform Aim; public WeaponController Weapon; public AbilityController Ability; public CharacterController Motor; public ActorVisual Visual; public MatchDirector Match; public float Shield {get;};
`void Initialize(MatchDirector match,int id,string name,int team,bool human); void Respawn(Vector3 position,Quaternion rotation); void ApplyDamage(float damage,Actor attacker,bool ultimate=false,bool ignoreArmor=false,string hitgroup="chest",float armorRatio=1.5f); void Heal(float amount);`
Actor.Initialize сам создает motor/weapons/playerorbotcontroller, Aim ивид. Root передRespawn назначит MaxHP/SpeedMultiplier/DamageMultiplier и Ability,Actor.Humanweaponlookupизprofile(ботыrootназначаетчерезEquipLoadout). SpawnProtection проверяетсячерезMatch.MatchTime. Shield->Ability.Shield.
`WeaponController`: public Actor Owner; public WeaponSpec Spec; public int Ammo,Reserve; public bool Reloading; public float ReloadProgress; `EquipLoadout(string primary,string pistol); SelectSlot(string slot); bool TryFire(); void Reload(); void Alternate();`
ApplyDamage ожидает повреждениепослеhitgroup/distance/strength/wood, применяетщиты/броню. `Ability.AbsorbDamage(float incoming)` возвращаетостаток; absoluteиспользуетignoreArmor+достаточныйурон через отдельныйзапрос `Ability.AbsoluteActive` (щит bypassвызывающийнечерезобычныйApplyDamage; добавьте Actor.Kill(attacker,true) сprotectioncheck).
`AbilityController` root: public Actor Owner; public string EquippedId; public bool Ready; public int Streak; public float StreakRemaining,ActiveRemaining; public bool AbsoluteActive,Blocked; public float Shield,SpeedFactor=1,ReloadFactor=1,StabilityFactor=1; `Initialize(Actor,string); ResetForSpawn(bool preserveReady); RegisterKill(bool fromUltimate); bool Activate(); bool FireTemporary(Vector3 origin,Vector3 direction); bool HasTemporaryWeapon; float AbsorbDamage(float damage); void OnDealtHealthDamage(float amount,bool ultimate); void ApplyLoot(string id);`
Temporaryweapon: TryFirefirstdelegatesAbility.FireTemporary; don'tconsumeordinaryammo. Ability itselfhandlesprojectilerootimplement. Ordinaryshots call OnDealtHealthDamage onlyactualHP,ultimateflag absolute. Owner death Ability reset активное иpartialstreak, DMready preserve.
Bots target толькоAliveenemies; aim reaction300ms, LOS; nowallcheat; use NavMeshAgent forpathdesiredvelocity, CharacterController Move forexecution. Player movementWASD/Shiftwalk/Ctrlcrouch/Spacejump,mouse,R,Q,E,F,slots1..4. Q invokesAbility.Activate. EschandledGameRoot/GameUI.

### World / visuals (art agent)
`ZooWorld : MonoBehaviour`: `static ZooWorld Build(Transform parent)` builds scene and NavMeshSurface; public Vector3[] TeamASpawns,TeamBSpawns,DeathmatchSpawns,LootPoints; `Vector3 SpawnPoint(int team,int index,bool deathmatch)`; public Transform MenuCameraAnchor (viewpoint), public Transform PreviewAnchor (weapon displayoffmapwithlight).
`ActorVisual`: `static ActorVisual Create(Transform parent,int team); void SetMotion(float speed,bool crouching); void SetDead(bool dead); void SetAim(float pitch);` ownsmodelwithoutcollidersexceptrootcombatHitZone, can exposeTransformHead/Chest optionalask.
`WeaponModelFactory.Create(Transform parent,WeaponSpec spec,SkinPreset skin,bool firstPerson)` -> `WeaponPresentation : MonoBehaviour`; presentation `void Fire(); void SetReload(float progress); void Inspect();` owns gun geometry; no resourcesfromCS2/Standoff. Previewcanusefactorysamecall. rootUIasksagentifAPImore.
Allsolidworldcolliders have `SurfaceTag` (rootdefines): public string MaterialId="concrete"; public int ObstacleId {get=>logicalGroup?logicalGroup.GetInstanceID():gameObject.GetInstanceID();}; public GameObject logicalGroup;
Primitivecreation okay as authored detailedassembledmeshes, but variety/silhouette/readabilitymatter. Use shared URP materials; runtime geometrybakednavmeshwithUnity.AI.Navigation. Do notuseexternalassetswithoutprovenance.

## Ограничения

Сначала workingstandaloneизоднойсцены+runtimebootstrap. Root создает Editor.BuildTasks.Setup/BuildWindows/Validate. Старые документыостаютсяисторическойспецификацией, README обновимсреальнымстатусом. Не заявлятьточнуюграфику1:1ипроверенные60FPSбезизмерений. Никакихвнешнихплатныхсервисов/аккаунтовбезнеобходимости. ИзмененияAPIсогласовыватьсоrootсразу.
