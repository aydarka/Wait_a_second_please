using UnityEngine;

// The sound of one enemy bullet, added at run time by SoundDirector to each bullet it sees in flight. Bullets come
// from a pool, so the voice starts again every time its bullet is launched and stops when the bullet goes back.
// - Air: a 3D loop of air tearing past, with a Doppler of its own computed in game time, so in slow motion the
//   pitch drop as it passes stretches out instead of vanishing.
// - Rings: the bullet leaves an air ring every 0.35 m. When rings come slowly enough to hear one by one (slow
//   motion), each ring gives a soft push ("вуб… вуб"); in normal time the loop's own buzz stands for them.
// - Near: for a bullet that will pass within the close distance, a roar beside the ear fades in ahead of the pass
//   (from the bullet's own predicted meeting) and out behind the head. Behind the head every layer is muffled.
[DefaultExecutionOrder(150)]
public sealed class BulletVoice : MonoBehaviour
{
    private Projectile bullet;
    private SoundDirector director;
    private SoundBank bank;
    private AudioSource air;
    private AudioSource near;
    private AudioSource pulse;
    private AudioLowPassFilter airFilter;
    private AudioLowPassFilter nearFilter;
    private bool flying;
    private Vector3 lastPosition;
    private Vector3 lastEyes;
    private Vector3 velocity;
    private Vector3 eyesVelocity;
    private float sinceRing;
    private bool closePass;
    private bool measured;

    // Called every frame while the bullet is on its way; starts the voice on the first call of each flight.
    public void Notice(SoundDirector owner)
    {
        if (flying || owner == null || owner.Bank == null) return;
        if (air == null) Build(owner);
        Begin();
    }

    private void Build(SoundDirector owner)
    {
        director = owner;
        bank = owner.Bank;
        bullet = GetComponent<Projectile>();
        air = NewLayer("Bullet Air", bank.bulletAir, true, 1.5f, 60f, 64, out airFilter);
        near = NewLayer("Bullet Near", bank.nearAir, true, 0.5f, 12f, 32, out nearFilter);
        pulse = NewLayer("Bullet Rings", bank.bulletPulse, false, 1.5f, 60f, 64, out _);
    }

    private AudioSource NewLayer(string name, SoundBank.Sound sound, bool loop, float minDistance, float maxDistance,
        int priority, out AudioLowPassFilter filter)
    {
        var holder = new GameObject(name);
        holder.transform.SetParent(transform, false);
        var source = holder.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.outputAudioMixerGroup = director.BulletsGroup;
        source.spatialBlend = 1f;
        source.dopplerLevel = 0f;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        source.priority = priority;
        source.loop = loop;
        source.clip = loop && sound.IsSet ? sound.clip : null;
        filter = loop ? holder.AddComponent<AudioLowPassFilter>() : null;
        if (filter != null) filter.cutoffFrequency = 22000f;
        return source;
    }

    private void Begin()
    {
        flying = true;
        var view = Camera.main;
        lastPosition = transform.position;
        lastEyes = view != null ? view.transform.position : lastPosition;
        velocity = Vector3.zero;
        eyesVelocity = Vector3.zero;
        sinceRing = 0f;
        closePass = false;
        measured = false;
        StartLoop(air);
        StartLoop(near);
    }

    private static void StartLoop(AudioSource source)
    {
        if (source.clip == null) return;
        source.volume = 0f;
        source.time = Random.Range(0f, source.clip.length * 0.95f);
        source.Play();
    }

    private void LateUpdate()
    {
        if (!flying || bullet == null) return;
        var view = Camera.main;
        if (view == null) return;

        float dt = Time.deltaTime;
        Vector3 position = transform.position;
        Vector3 eyes = view.transform.position;
        float flown = (position - lastPosition).magnitude;
        if (dt > 0f && flown > 0f)
        {
            velocity = (position - lastPosition) / dt;
            eyesVelocity = (eyes - lastEyes) / dt;
            measured = true;
        }
        lastPosition = position;
        lastEyes = eyes;
        // Until the bullet has moved once its speed is unknown, and a zero speed would read as a bullet at the ear.
        if (!measured) return;

        // Doppler from the closing speed in game time.
        Vector3 toEars = eyes - position;
        float distance = toEars.magnitude;
        Vector3 towardEars = distance > 0.0001f ? toEars / distance : view.transform.forward * -1f;
        float closing = Vector3.Dot(velocity - eyesVelocity, towardEars);
        float c = Mathf.Max(1f, bank.dopplerSoundSpeed);
        float doppler = Mathf.Clamp(c / Mathf.Max(c * 0.2f, c - closing), bank.dopplerPitchLimits.x, bank.dopplerPitchLimits.y);
        air.pitch = doppler;
        near.pitch = doppler;
        pulse.pitch = doppler;

        // Rings heard one by one once they come slowly enough.
        float pulseSpacing = Mathf.Max(0.01f, bank.ringSpacing) * Mathf.Max(1, bank.ringsPerPulse);
        float pulsesPerSecond = velocity.magnitude * Time.timeScale / pulseSpacing;
        float pulses = 1f - Mathf.InverseLerp(bank.pulseRateFade.x, bank.pulseRateFade.y, pulsesPerSecond);
        air.volume = bank.bulletAir.volume * Mathf.Lerp(1f, 0.3f, pulses);
        sinceRing += flown;
        if (sinceRing >= pulseSpacing)
        {
            sinceRing %= pulseSpacing;
            if (pulses > 0.02f && bank.bulletPulse.IsSet)
                pulse.PlayOneShot(bank.bulletPulse.clip, bank.bulletPulse.volume * pulses);
        }

        // Behind the head every layer is muffled, so the pass is heard as front to back.
        float ahead = Vector3.Dot(view.transform.forward, -towardEars);
        float behind = Mathf.InverseLerp(0.1f, -0.4f, ahead);
        float cutoff = Mathf.Lerp(22000f, bank.behindCutoff, behind);
        airFilter.cutoffFrequency = cutoff;
        nearFilter.cutoffFrequency = cutoff;

        // The roar beside the ear, for a bullet that will pass within the close distance.
        float nearLevel = 0f;
        if (bullet.PredictMeeting(view, out float secondsToEyes, out float miss, out _))
        {
            closePass = miss <= bullet.CloseDistance;
            if (closePass)
            {
                // Game seconds to the eyes times the closing speed: metres still to go.
                float metresToEyes = secondsToEyes * Mathf.Max(0f, closing);
                nearLevel = 1f - Mathf.Clamp01(metresToEyes / Mathf.Max(0.01f, bank.nearLeadMetres));
                if (metresToEyes <= bank.closePassLeadMetres) director.HoldCloseFocus();
            }
        }
        else if (closePass)
        {
            float behindMetres = Mathf.Max(0f, Vector3.Dot(-toEars, -view.transform.forward));
            nearLevel = 1f - Mathf.Clamp01(behindMetres / Mathf.Max(0.01f, bank.nearTail));
        }
        near.volume = bank.nearAir.volume * nearLevel;
    }

    private void OnDisable()
    {
        flying = false;
        if (air != null) air.Stop();
        if (near != null) near.Stop();
        if (pulse != null) pulse.Stop();
    }
}
