using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

// The game's sound in one place. It creates itself in every scene with a GameSession, listens to the game's events
// and runs the mix through the GameMixer:
// - World and Bullets are pitched down and muffled with the slow-down (from Time.timeScale, in log space, so it
//   follows how slow time really is); the bullets are muffled less, so the bullet stays sharp while the world recedes.
// - Player sounds (pistol, ear ring, slow-motion in/out, clock, death) stay in real time, as in Superhot.
// - Music stays in tune and in real time but recedes in slow motion; a low bed and a heartbeat come in underneath.
// - Focus moments duck the rest on top: a close pass (known ahead from the bullet's predicted meeting) and the tap
//   window, where the clock is the one clear sound.
// Clips and every level live in the SoundBank asset (Resources/Audio/SoundBank).
[DefaultExecutionOrder(200)]
public sealed class SoundDirector : MonoBehaviour
{
    private const string BankResource = "Audio/SoundBank";
    private const int WorldVoiceCount = 8;
    private const float Silent = -80f;
    private const float OpenCutoff = 22000f;

    private static SoundDirector instance;

    private SoundBank bank;
    private AudioMixer mixer;
    private AudioMixerGroup worldGroup;
    private AudioMixerGroup bulletsGroup;
    private AudioMixerGroup playerGroup;
    private AudioMixerGroup musicGroup;
    private readonly List<AudioSource> worldVoices = new List<AudioSource>();
    private int nextWorldVoice;
    private AudioSource player;
    private AudioSource playerRight;
    private AudioSource ear;
    private AudioSource clock;
    private AudioSource later;
    private AudioSource bed;
    private AudioSource heart;
    private AudioSource music;

    private float closeFocus;
    private float tapFocus;
    private float closeFocusUntil = float.NegativeInfinity;
    private bool tapWindowOpen;
    private Coroutine clockRun;
    private float lastSlowMotionEnter = float.NegativeInfinity;
    private bool enterWasHeard;
    private bool dead;
    private float deathTime;

    internal SoundBank Bank => bank;
    internal AudioMixerGroup BulletsGroup => bulletsGroup;

    // How deep the slow-down is: 0 in normal time, 1 at the bank's deepest time scale or slower.
    public static float Depth { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        Depth = 0f;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Ensure();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Ensure();

    // Only game scenes get sound: a scene without a GameSession is left alone.
    private static void Ensure()
    {
        if (instance != null || FindAnyObjectByType<GameSession>() == null) return;
        new GameObject("Sound Director").AddComponent<SoundDirector>();
    }

    private void Awake()
    {
        instance = this;
        bank = Resources.Load<SoundBank>(BankResource);
        mixer = bank != null ? bank.mixer : null;
        if (mixer == null)
        {
            Debug.LogError("SoundDirector needs Resources/Audio/SoundBank with its mixer set (Tools > Sound).", this);
            enabled = false;
            return;
        }
        worldGroup = Group("World");
        bulletsGroup = Group("Bullets");
        playerGroup = Group("Player");
        musicGroup = Group("Music");

        for (int i = 0; i < WorldVoiceCount; i++) worldVoices.Add(NewSource("World Voice " + i, worldGroup, true));
        player = NewSource("Player", playerGroup, false);
        playerRight = NewSource("Player Right", playerGroup, false);
        playerRight.panStereo = 0.35f;
        ear = NewSource("Ear", playerGroup, false);
        clock = NewSource("Clock", playerGroup, false);
        later = NewSource("Delayed", playerGroup, false);
        bed = NewLoop("Slow-Motion Bed", bank.slowMotionBed, playerGroup);
        heart = NewLoop("Heartbeat", bank.heartbeat, playerGroup);
        music = NewLoop("Music", bank.music, musicGroup);
        if (music != null) music.volume = bank.music.volume;
    }

    private void OnEnable()
    {
        EnemyGunman.ShotFired += OnEnemyShot;
        Projectile.PassedPlayer += OnBulletPassed;
        Projectile.HitPlayer += OnPlayerHit;
        Projectile.HitCover += OnCoverHit;
        CounterShot.Fired += OnCounterShotFired;
        CounterShot.Landed += OnCounterShotLanded;
        CounterShot.TapWindowOpened += OnTapWindowOpened;
        CounterShot.TapWindowClosed += OnTapWindowClosed;
        TimeScaleController.SlowMotionEntered += OnSlowMotionEntered;
        TimeScaleController.SlowMotionExited += OnSlowMotionExited;
    }

    private void OnDisable()
    {
        EnemyGunman.ShotFired -= OnEnemyShot;
        Projectile.PassedPlayer -= OnBulletPassed;
        Projectile.HitPlayer -= OnPlayerHit;
        Projectile.HitCover -= OnCoverHit;
        CounterShot.Fired -= OnCounterShotFired;
        CounterShot.Landed -= OnCounterShotLanded;
        CounterShot.TapWindowOpened -= OnTapWindowOpened;
        CounterShot.TapWindowClosed -= OnTapWindowClosed;
        TimeScaleController.SlowMotionEntered -= OnSlowMotionEntered;
        TimeScaleController.SlowMotionExited -= OnSlowMotionExited;
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
            Depth = 0f;
        }
    }

    private void Update()
    {
        if (!dead) GiveBulletsVoices();
        UpdateMix();
    }

    // Bullets come from a pool and are launched by the enemies' own code, so each one in flight gets its voice here.
    private void GiveBulletsVoices()
    {
        foreach (var bullet in Projectile.Approaching)
        {
            if (bullet == null) continue;
            if (!bullet.TryGetComponent(out BulletVoice voice)) voice = bullet.gameObject.AddComponent<BulletVoice>();
            voice.Notice(this);
        }
    }

    // A close bullet asks for the focus duck while it is about to pass; it lasts a moment after the last request.
    internal void HoldCloseFocus(float seconds = 0.1f)
    {
        closeFocusUntil = Mathf.Max(closeFocusUntil, Time.unscaledTime + seconds);
    }

    private void UpdateMix()
    {
        float now = Time.unscaledTime;
        float depth = dead ? 1f : DepthAt(Time.timeScale);
        Depth = depth;

        float pitch = Mathf.Exp(Mathf.Lerp(0f, Mathf.Log(Mathf.Clamp(bank.deepestPitch, 0.1f, 1f)), depth));
        float musicPitch = 1f;
        float worldCutoff = LogLerp(OpenCutoff, bank.worldCutoffAtDeepest, depth);
        float bulletCutoff = LogLerp(OpenCutoff, bank.bulletCutoffAtDeepest, depth);
        float musicCutoff = LogLerp(OpenCutoff, bank.musicCutoffAtDeepest, depth);

        closeFocus = Approach(closeFocus, now < closeFocusUntil ? 1f : 0f);
        tapFocus = Approach(tapFocus, tapWindowOpen ? 1f : 0f);
        float musicDb = depth * bank.musicDuckAtDeepest +
                        Mathf.Min(closeFocus * bank.closePassMusicDuck, tapFocus * bank.tapWindowMusicDuck);
        float worldDb = Mathf.Min(closeFocus * bank.closePassWorldDuck, tapFocus * bank.tapWindowWorldDuck);
        float bulletsDb = 0f;
        float bedLevel = depth;

        if (dead)
        {
            // Time has stopped: the world and the music run down like tape, then fall silent.
            float t = Mathf.Clamp01((now - deathTime) / Mathf.Max(0.01f, bank.tapeStopSeconds));
            float tape = Mathf.Max(0.01f, (1f - t) * (1f - t));
            float fade = Mathf.Lerp(0f, Silent, Mathf.InverseLerp(0.6f, 1f, t));
            pitch = Mathf.Max(0.01f, pitch * tape);
            musicPitch = tape;
            worldDb += fade;
            bulletsDb += fade;
            musicDb += fade;
            bedLevel *= 1f - t;
        }

        Set("WorldPitch", pitch);
        Set("BulletsPitch", pitch);
        Set("MusicPitch", musicPitch);
        Set("WorldCutoff", worldCutoff);
        Set("BulletsCutoff", bulletCutoff);
        Set("MusicCutoff", musicCutoff);
        Set("WorldVolume", worldDb);
        Set("BulletsVolume", bulletsDb);
        Set("MusicVolume", Mathf.Max(Silent, musicDb));

        if (bed != null) bed.volume = bank.slowMotionBed.volume * bedLevel;
        if (heart != null) heart.volume = bank.heartbeat.volume * Mathf.InverseLerp(bank.heartbeatFromDepth, 1f, bedLevel);
    }

    private float DepthAt(float timeScale)
    {
        if (timeScale >= 1f) return 0f;
        float deepest = Mathf.Clamp(bank.deepestTimeScale, 0.001f, 0.99f);
        return Mathf.Clamp01(Mathf.Log(Mathf.Max(timeScale, 0.0001f)) / Mathf.Log(deepest));
    }

    private float Approach(float current, float target)
    {
        float seconds = target > current ? bank.focusAttack : bank.focusRelease;
        return seconds > 0f ? Mathf.MoveTowards(current, target, Time.unscaledDeltaTime / seconds) : target;
    }

    private static float LogLerp(float from, float to, float t) =>
        Mathf.Exp(Mathf.Lerp(Mathf.Log(Mathf.Max(10f, from)), Mathf.Log(Mathf.Max(10f, to)), t));

    private void Set(string parameter, float value) => mixer.SetFloat(parameter, value);

    // ------------------------------------------------------------------ events

    private void OnEnemyShot(EnemyGunman enemy)
    {
        if (dead || enemy == null) return;
        Vector3 at = enemy.Muzzle != null ? enemy.Muzzle.position : enemy.transform.position;
        bool far = Vector3.Distance(at, EarsPosition) > bank.farShotDistance;
        PlayAt(far ? bank.enemyShotFar : bank.enemyShotNear, at, worldGroup, 8f, 90f);
    }

    private void OnBulletPassed(Projectile bullet, bool close)
    {
        if (!close || dead || bullet == null) return;
        HoldCloseFocus(0.25f);
        PlayAt(bank.nearWhump, bullet.transform.position, bulletsGroup, 0.6f, 20f);
        // The ear on the side the bullet passed rings; the pistol comes up for the counter-shot.
        ear.panStereo = SideOf(bullet.transform.position) * 0.6f;
        PlayDelayed(ear, bank.earRing, 0.08f);
        PlayOn(playerRight, bank.gunDraw);
    }

    private void OnPlayerHit(Projectile bullet)
    {
        if (dead) return;
        dead = true;
        deathTime = Time.unscaledTime;
        StopClock();
        tapWindowOpen = false;
        PlayOn(player, bank.deathImpact);
        PlayOn(player, bank.deathSwell);
        ear.panStereo = 0f;
        PlayDelayed(ear, bank.earRing, 0.05f);
        PlayDelayed(later, bank.deathChord, bank.deathChordDelay);
    }

    private void OnCoverHit(Projectile bullet, Vector3 point)
    {
        if (dead) return;
        PlayAt(IsMetal(point) ? bank.ricochetMetal : bank.ricochetConcrete, point, worldGroup, 4f, 70f);
    }

    private void OnCounterShotFired(Vector3 muzzle)
    {
        if (dead) return;
        PlayOn(playerRight, bank.pistolShot);
    }

    private void OnCounterShotLanded(EnemyGunman enemy)
    {
        if (dead || enemy == null) return;
        // Stylised: the hit carries to the player from 15–36 m away.
        PlayAt(bank.enemyHit, enemy.TargetPoint, worldGroup, 15f, 120f);
        PlayDelayed(later, bank.hitNote, 0.04f);
        StartCoroutine(PlayAfter(playerRight, bank.gunHolster, 0.3f));
    }

    private void OnTapWindowOpened(float realSeconds)
    {
        if (dead) return;
        tapWindowOpen = true;
        PlayOn(player, bank.tapFocus);
        StopClock();
        clockRun = StartCoroutine(RunClock(realSeconds));
    }

    private void OnTapWindowClosed(bool fired)
    {
        tapWindowOpen = false;
        StopClock();
        if (fired || dead) return;
        PlayOn(player, bank.tapMiss);
        StartCoroutine(PlayAfter(playerRight, bank.gunHolster, 0.1f));
    }

    // Slow-motion in and out are heard only in TIME: DANGER, where they are the moments a threat begins and ends;
    // in TIME: TILT time crosses the same thresholds with every tilt, so only the continuous pitch and filter remain.
    private void OnSlowMotionEntered()
    {
        enterWasHeard = false;
        if (dead || ControlOptions.TimeControl != ControlOptions.TimeControlMode.Danger) return;
        if (Time.unscaledTime - lastSlowMotionEnter < bank.slowMotionSoundCooldown) return;
        lastSlowMotionEnter = Time.unscaledTime;
        enterWasHeard = true;
        PlayOn(player, bank.slowMotionEnter);
    }

    private void OnSlowMotionExited()
    {
        if (dead || !enterWasHeard) return;
        enterWasHeard = false;
        PlayOn(player, bank.slowMotionExit);
    }

    // The tap window's pocket watch: tick, tock, faster and faster as the yellow ring closes.
    private IEnumerator RunClock(float realSeconds)
    {
        float start = Time.unscaledTime;
        bool tick = true;
        while (tapWindowOpen && !dead)
        {
            PlayOn(clock, tick ? bank.clockTick : bank.clockTock);
            tick = !tick;
            float progress = Mathf.Clamp01((Time.unscaledTime - start) / Mathf.Max(0.1f, realSeconds));
            yield return Unscaled(Mathf.Lerp(bank.clockInterval.x, bank.clockInterval.y, progress * progress));
        }
        clockRun = null;
    }

    private void StopClock()
    {
        if (clockRun != null) StopCoroutine(clockRun);
        clockRun = null;
    }

    // ------------------------------------------------------------------ playing

    private void PlayOn(AudioSource source, SoundBank.Sound sound)
    {
        if (source == null || sound == null || !sound.IsSet) return;
        source.pitch = sound.RandomPitch;
        source.PlayOneShot(sound.clip, sound.volume);
    }

    private static void PlayDelayed(AudioSource source, SoundBank.Sound sound, float seconds)
    {
        if (source == null || sound == null || !sound.IsSet) return;
        source.clip = sound.clip;
        source.volume = sound.volume;
        source.pitch = sound.RandomPitch;
        source.PlayDelayed(seconds);
    }

    private IEnumerator PlayAfter(AudioSource source, SoundBank.Sound sound, float seconds)
    {
        yield return Unscaled(seconds);
        if (!dead) PlayOn(source, sound);
    }

    // Waits on Time.unscaledTime, the clock the tap window and the mix run on.
    private static IEnumerator Unscaled(float seconds)
    {
        float until = Time.unscaledTime + seconds;
        while (Time.unscaledTime < until) yield return null;
    }

    // A one-shot at a point in the world, on a free voice or, if all are busy, on the oldest one.
    private void PlayAt(SoundBank.Sound sound, Vector3 position, AudioMixerGroup group, float minDistance, float maxDistance)
    {
        if (sound == null || !sound.IsSet) return;
        AudioSource voice = null;
        for (int i = 0; i < worldVoices.Count && voice == null; i++)
        {
            var candidate = worldVoices[(nextWorldVoice + i) % worldVoices.Count];
            if (!candidate.isPlaying) voice = candidate;
        }
        if (voice == null) voice = worldVoices[nextWorldVoice];
        nextWorldVoice = (worldVoices.IndexOf(voice) + 1) % worldVoices.Count;

        voice.transform.position = position;
        voice.outputAudioMixerGroup = group;
        voice.minDistance = minDistance;
        voice.maxDistance = maxDistance;
        voice.clip = sound.clip;
        voice.volume = sound.volume;
        voice.pitch = sound.RandomPitch;
        voice.Play();
    }

    // ------------------------------------------------------------------ helpers

    private static Vector3 EarsPosition => Camera.main != null ? Camera.main.transform.position : Vector3.zero;

    // -1 when the point is to the player's left, +1 to the right.
    private static float SideOf(Vector3 point)
    {
        var view = Camera.main;
        if (view == null) return 0f;
        return Mathf.Sign(Vector3.Dot(point - view.transform.position, view.transform.right));
    }

    // Cars ring like metal; everything else (blocks, parapets, door piers) is concrete. The street's cover pieces are
    // all named "Piece", so a car is told by its mesh (Car_hollow) or material (NoirCar) from the street generator.
    private static readonly Collider[] nearby = new Collider[8];
    private static bool IsMetal(Vector3 point)
    {
        int count = Physics.OverlapSphereNonAlloc(point, 0.5f, nearby, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var piece = nearby[i];
            if (Named(piece.name)) return true;
            if (piece.TryGetComponent(out MeshFilter mesh) && mesh.sharedMesh != null && Named(mesh.sharedMesh.name)) return true;
            if (piece.TryGetComponent(out Renderer look) && look.sharedMaterial != null && Named(look.sharedMaterial.name)) return true;
        }
        return false;
    }

    private static bool Named(string name) => name.IndexOf("car", System.StringComparison.OrdinalIgnoreCase) >= 0;

    private AudioMixerGroup Group(string name)
    {
        var groups = mixer.FindMatchingGroups(name);
        foreach (var group in groups)
            if (group.name == name) return group;
        Debug.LogError("GameMixer has no group " + name, this);
        return null;
    }

    private AudioSource NewSource(string name, AudioMixerGroup group, bool spatial)
    {
        var holder = new GameObject(name);
        holder.transform.SetParent(transform, false);
        var source = holder.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.outputAudioMixerGroup = group;
        source.spatialBlend = spatial ? 1f : 0f;
        source.dopplerLevel = 0f;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.priority = spatial ? 96 : 16;
        return source;
    }

    private AudioSource NewLoop(string name, SoundBank.Sound sound, AudioMixerGroup group)
    {
        if (sound == null || !sound.IsSet) return null;
        var source = NewSource(name, group, false);
        source.clip = sound.clip;
        source.loop = true;
        source.volume = 0f;
        source.priority = 8;
        source.Play();
        return source;
    }
}
