using System;
using UnityEngine;
using UnityEngine.Audio;

// Every sound of the game and the mix rules in one asset (Resources/Audio/SoundBank), so clips can be swapped and
// levels tuned in the inspector without touching code. SoundDirector reads it at the start of each run.
[CreateAssetMenu(menuName = "Sound/Sound Bank", fileName = "SoundBank")]
public sealed class SoundBank : ScriptableObject
{
    [Serializable]
    public sealed class Sound
    {
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 1f;
        [Range(0f, 0.3f), Tooltip("Random pitch spread per play, as a share of the pitch, so repeats do not sound identical.")]
        public float pitchSpread;

        public bool IsSet => clip != null;
        public float RandomPitch => 1f + UnityEngine.Random.Range(-pitchSpread, pitchSpread);
    }

    [Header("Mixer (groups World, Bullets, Player, Music)")]
    public AudioMixer mixer;

    [Header("Enemies and the street (World group, pitched down in slow motion)")]
    public Sound enemyShotNear = new Sound();
    public Sound enemyShotFar = new Sound();
    [Tooltip("Distance in metres beyond which an enemy shot uses the far variant.")]
    public float farShotDistance = 24f;
    public Sound enemyHit = new Sound();
    public Sound ricochetMetal = new Sound();
    public Sound ricochetConcrete = new Sound();

    [Header("Enemy bullets (Bullets group: pitched like the world, muffled less)")]
    [Tooltip("Loop of air tearing past the bullet; buzzes at the ring rate in normal time.")]
    public Sound bulletAir = new Sound();
    [Tooltip("One soft push per air ring once rings are slow enough to hear one by one.")]
    public Sound bulletPulse = new Sound();
    [Tooltip("Loop of the roar right beside the ear, faded in ahead of a close pass.")]
    public Sound nearAir = new Sound();
    [Tooltip("Pressure push at the moment a close bullet crosses the ear.")]
    public Sound nearWhump = new Sound();

    [Header("Player (Player group: real time, never slowed)")]
    public Sound gunDraw = new Sound();
    public Sound gunHolster = new Sound();
    public Sound pistolShot = new Sound();
    public Sound earRing = new Sound();
    public Sound slowMotionEnter = new Sound();
    public Sound slowMotionExit = new Sound();
    public Sound tapFocus = new Sound();
    public Sound clockTick = new Sound();
    public Sound clockTock = new Sound();
    public Sound tapMiss = new Sound();
    [Tooltip("Short musical note in the music's key when the counter-shot lands.")]
    public Sound hitNote = new Sound();
    public Sound deathImpact = new Sound();
    public Sound deathSwell = new Sound();
    public Sound deathChord = new Sound();

    [Header("Slow-motion bed (Player group) and music (Music group)")]
    public Sound slowMotionBed = new Sound();
    public Sound heartbeat = new Sound();
    public Sound music = new Sound();

    [Header("Slow motion")]
    [Tooltip("Time scale treated as the deepest slow-down; slower still counts as the deepest.")]
    public float deepestTimeScale = 0.05f;
    [Tooltip("Pitch of the world and the bullets at the deepest slow-down (0.5 = an octave lower).")]
    [Range(0.1f, 1f)] public float deepestPitch = 0.5f;
    [Tooltip("Low-pass cutoff of the world at the deepest slow-down, Hz.")]
    public float worldCutoffAtDeepest = 1200f;
    [Tooltip("Low-pass cutoff of the bullets at the deepest slow-down, Hz: higher than the world, so the bullet stays sharp.")]
    public float bulletCutoffAtDeepest = 4000f;
    [Tooltip("Low-pass cutoff of the music at the deepest slow-down, Hz.")]
    public float musicCutoffAtDeepest = 500f;
    [Tooltip("Music level at the deepest slow-down, dB.")]
    public float musicDuckAtDeepest = -8f;
    [Tooltip("Slow-motion depth (0 to 1) from which the heartbeat comes in.")]
    [Range(0f, 1f)] public float heartbeatFromDepth = 0.45f;
    [Tooltip("Real seconds before the in/out sounds may play again.")]
    public float slowMotionSoundCooldown = 1f;

    [Header("Focus moments (on top of the slow-motion duck)")]
    [Tooltip("Metres before the eyes at which a close bullet starts the focus duck (bullets fly 70 m/s: about the last second of a deep slow-down).")]
    public float closePassLeadMetres = 5f;
    public float closePassMusicDuck = -6f;
    public float closePassWorldDuck = -4f;
    public float tapWindowMusicDuck = -15f;
    public float tapWindowWorldDuck = -6f;
    [Tooltip("Real seconds for a focus duck to come in.")]
    public float focusAttack = 0.12f;
    [Tooltip("Real seconds for a focus duck to go away.")]
    public float focusRelease = 0.5f;

    [Header("Bullet voice")]
    [Tooltip("Distance between two air rings along the flight, metres (AirRingTrail spacing).")]
    public float ringSpacing = 0.35f;
    [Tooltip("Air rings per heard push: at 70 m/s the deepest slow-down makes about 10 rings a real second, so every second ring gives one.")]
    [Min(1)] public int ringsPerPulse = 2;
    [Tooltip("Pushes per real second: below x they are heard one by one, above y only the loop's buzz remains.")]
    public Vector2 pulseRateFade = new Vector2(6f, 14f);
    [Tooltip("Stylised speed of sound for the bullet's Doppler, metres per game second; lower bends the pitch more. Bullets close at about 76 m/s.")]
    public float dopplerSoundSpeed = 300f;
    public Vector2 dopplerPitchLimits = new Vector2(0.6f, 1.6f);
    [Tooltip("Metres before the eyes over which the near-ear roar fades in for a close bullet.")]
    public float nearLeadMetres = 6f;
    [Tooltip("Metres behind the eyes over which the near-ear roar fades out after the pass.")]
    public float nearTail = 3f;
    [Tooltip("Low-pass cutoff of a bullet once it is behind the head, Hz.")]
    public float behindCutoff = 2500f;

    [Header("Tap window clock")]
    [Tooltip("Real seconds between ticks at the start and at the end of the tap window.")]
    public Vector2 clockInterval = new Vector2(0.5f, 0.14f);

    [Header("Death")]
    [Tooltip("Real seconds for the world and the music to stop like tape.")]
    public float tapeStopSeconds = 0.3f;
    [Tooltip("Real seconds after the hit when the chord lands (the red has closed on the shooter).")]
    public float deathChordDelay = 0.35f;
}
