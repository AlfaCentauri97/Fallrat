using UnityEngine;
using Project.Core;
using System.Collections;

public class AudioManager : SingletonMonoBehaviour<AudioManager>
{
    public AudioSource backgroundAudio;
    public AudioSource hitEffectAudio;

    [Header("Background settings")]
    [SerializeField] private float fadeDuration = 1f;
    [SerializeField] private float targetMusicVolume = 0.7f;

    private AudioClip[] backgroundPlaylist;
    private int currentTrackIndex = 0;

    private Coroutine musicRoutine;

    private void Start()
    {
        LoadBackgroundPlaylist();
        
        backgroundAudio.loop = false;

        if (backgroundPlaylist != null && backgroundPlaylist.Length > 0)
            musicRoutine = StartCoroutine(BackgroundMusicLoop());
    }

    // BACKGROUND MUSIC
    private void LoadBackgroundPlaylist()
    {
        backgroundPlaylist = Resources.LoadAll<AudioClip>("Audio/Background");
    }

    private IEnumerator BackgroundMusicLoop()
    {
        backgroundAudio.volume = 0f;

        while (true)
        {
            backgroundAudio.clip = backgroundPlaylist[currentTrackIndex];
            backgroundAudio.Play();
            
            yield return FadeVolume(backgroundAudio, 0f, targetMusicVolume, fadeDuration);
            
            float timeToWait = Mathf.Max(0f, backgroundAudio.clip.length - fadeDuration);
            yield return new WaitForSeconds(timeToWait);
            
            yield return FadeVolume(backgroundAudio, targetMusicVolume, 0f, fadeDuration);

            backgroundAudio.Stop();
            
            currentTrackIndex++;
            if (currentTrackIndex >= backgroundPlaylist.Length)
                currentTrackIndex = 0;
        }
    }

    private IEnumerator FadeVolume(AudioSource source, float from, float to, float duration)
    {
        float t = 0f;
        source.volume = from;

        if (duration <= 0f)
        {
            source.volume = to;
            yield break;
        }

        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);
            source.volume = Mathf.Lerp(from, to, k);
            yield return null;
        }

        source.volume = to;
    }

    // HIT EFFECT
    public void PlayHitEffect(string clipName)
    {
        AudioClip clip = Resources.Load<AudioClip>("Audio/HitEffects/" + clipName);

        if (clip == null)
        {
            Debug.LogWarning($"HitEffect audio clip not found: {clipName}");
            return;
        }

        float randomPitch = Random.Range(0.9f, 1.1f);
        hitEffectAudio.pitch = randomPitch;

        hitEffectAudio.PlayOneShot(clip);
    }
}
