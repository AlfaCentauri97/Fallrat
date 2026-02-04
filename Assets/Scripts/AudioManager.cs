using UnityEngine;
using Project.Core;
using System.Collections;
using System.Collections.Generic;

public class AudioManager : SingletonMonoBehaviour<AudioManager>
{
    public AudioSource backgroundAudio;
    public AudioSource hitEffectAudio;

    [Header("Background settings")]
    [SerializeField] private float fadeDuration = 1f;
    [SerializeField] private float targetMusicVolume = 0.7f;

    private List<AudioClip> shuffledPlaylist;
    private int currentIndex = 0;

    private void Start()
    {
        backgroundAudio.loop = false;
        LoadAndShufflePlaylist();
        StartCoroutine(BackgroundMusicLoop());
    }
    
    // BACKGROUND MUSIC
    private void LoadAndShufflePlaylist()
    {
        AudioClip[] clips = Resources.LoadAll<AudioClip>("Audio/Background");

        shuffledPlaylist = new List<AudioClip>(clips);
        Shuffle(shuffledPlaylist);
        currentIndex = 0;
    }

    private IEnumerator BackgroundMusicLoop()
    {
        backgroundAudio.volume = 0f;

        while (true)
        {
            if (currentIndex >= shuffledPlaylist.Count)
            {
                Shuffle(shuffledPlaylist);
                currentIndex = 0;
            }

            AudioClip clip = shuffledPlaylist[currentIndex];
            currentIndex++;

            backgroundAudio.clip = clip;
            backgroundAudio.Play();
            
            yield return FadeVolume(0f, targetMusicVolume, fadeDuration);
            
            yield return new WaitForSeconds(
                Mathf.Max(0f, clip.length - fadeDuration)
            );
            
            yield return FadeVolume(targetMusicVolume, 0f, fadeDuration);

            backgroundAudio.Stop();
        }
    }

    private IEnumerator FadeVolume(float from, float to, float duration)
    {
        float t = 0f;
        backgroundAudio.volume = from;

        while (t < duration)
        {
            t += Time.deltaTime;
            backgroundAudio.volume = Mathf.Lerp(from, to, t / duration);
            yield return null;
        }

        backgroundAudio.volume = to;
    }

    private void Shuffle(List<AudioClip> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
    
    // HIT EFFECT
    public void PlayHitEffect(string clipName, float volume)
    {
        AudioClip clip = Resources.Load<AudioClip>("Audio/HitEffects/" + clipName);
        if (clip == null) return;

        hitEffectAudio.pitch = Random.Range(0.9f, 1.1f);
        hitEffectAudio.PlayOneShot(clip, Mathf.Clamp01(volume));
    }

}
