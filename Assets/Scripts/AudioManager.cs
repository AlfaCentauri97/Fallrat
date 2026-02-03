using UnityEngine;
using Project.Core;
using System.Collections;

public class AudioManager : SingletonMonoBehaviour<AudioManager>
{
    public AudioSource backgroundAudio;
    public AudioSource hitEffectAudio;

    private AudioClip[] backgroundPlaylist;
    private int currentTrackIndex = 0;

    private void Start()
    {
        LoadBackgroundPlaylist();
        PlayBackgroundMusic();
    }
    
    // BACKGROUND MUSIC
    private void LoadBackgroundPlaylist()
    {
        backgroundPlaylist = Resources.LoadAll<AudioClip>("Audio/Background");
    }

    private void PlayBackgroundMusic()
    {
        if (backgroundPlaylist.Length == 0)
            return;

        backgroundAudio.clip = backgroundPlaylist[currentTrackIndex];
        backgroundAudio.Play();

        StartCoroutine(WaitForTrackEnd());
    }

    private IEnumerator WaitForTrackEnd()
    {
        while (backgroundAudio.isPlaying)
            yield return null;

        NextTrack();
    }

    private void NextTrack()
    {
        currentTrackIndex++;

        if (currentTrackIndex >= backgroundPlaylist.Length)
            currentTrackIndex = 0;

        PlayBackgroundMusic();
    }
    
    // HIT EFFECT
    public void PlayHitEffect(string clipName)
    {
        AudioClip clip = Resources.Load<AudioClip>("Audio/HitEffects/" + clipName);

        hitEffectAudio.PlayOneShot(clip);
    }
}