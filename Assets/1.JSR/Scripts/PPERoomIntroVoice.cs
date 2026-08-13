using System.Collections;
using UnityEngine;

/// <summary>
/// Plays the PPE room intro voice once when the scene starts.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public class PPERoomIntroVoice : MonoBehaviour
{
    const string ResourcesClipPath = "PPE/PPE_RoomIntroVoice";

    [SerializeField] AudioClip introClip;
    [SerializeField] [Min(0f)] float startDelay;
    [SerializeField] [Range(0f, 1f)] float volume = 1f;
    [SerializeField] bool playOnStart = true;

    AudioSource audioSource;
    bool played;

    void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 0f; // 2D VO
        audioSource.volume = volume;

        if (introClip == null)
            introClip = Resources.Load<AudioClip>(ResourcesClipPath);
    }

    void Start()
    {
        if (playOnStart)
            PlayIntro();
    }

    public void PlayIntro()
    {
        if (played)
            return;

        if (introClip == null)
            introClip = Resources.Load<AudioClip>(ResourcesClipPath);

        if (introClip == null)
        {
            Debug.LogWarning(
                "[PPE Intro Voice] Missing clip. Place MP3 at Resources/PPE/PPE_RoomIntroVoice.mp3",
                this);
            return;
        }

        played = true;
        if (startDelay <= 0f)
            PlayNow();
        else
            StartCoroutine(PlayAfterDelay());
    }

    IEnumerator PlayAfterDelay()
    {
        yield return new WaitForSeconds(startDelay);
        PlayNow();
    }

    void PlayNow()
    {
        if (audioSource == null || introClip == null)
            return;

        audioSource.clip = introClip;
        audioSource.volume = volume;
        audioSource.Play();
        Debug.Log($"[PPE Intro Voice] Playing '{introClip.name}'", this);
    }
}
