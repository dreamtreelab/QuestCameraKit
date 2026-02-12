using System;
using System.Collections;
using UnityEngine;
using LiveKit;

namespace QuestCameraKit.WebRTC
{
    public class QuestMicrophoneSource : RtcAudioSource
    {
        private readonly GameObject _sourceObject;
        private readonly string _deviceName;
        private readonly AudioClip _existingClip;
        private bool _started = false;
        private MonoBehaviour _coroutineHelper;

        public override event Action<float[], int, int> AudioRead;

        public QuestMicrophoneSource(string deviceName, GameObject sourceObject, int channels, AudioClip existingClip = null) 
            : base(channels, RtcAudioSourceType.AudioSourceMicrophone)
        {
            _deviceName = deviceName;
            _sourceObject = sourceObject;
            _existingClip = existingClip;
            Debug.Log($"[QuestMicrophoneSource] Created with {channels} channels (using existing clip: {existingClip != null})");
        }

        public override void Start()
        {
            base.Start();
            if (_started) return;

            if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
                throw new InvalidOperationException("Microphone access not authorized");

            _coroutineHelper = _sourceObject.AddComponent<CoroutineHelper>();
            _coroutineHelper.StartCoroutine(StartMicrophone());

            _started = true;
        }

        private IEnumerator StartMicrophone()
        {
            AudioClip clip;
            
            if (_existingClip != null)
            {
                // Use the already-running microphone
                clip = _existingClip;
                Debug.Log($"[QuestMicrophoneSource] Using existing mic clip: {clip.channels}ch @ {clip.frequency}Hz");
            }
            else
            {
                // Start new microphone
                clip = Microphone.Start(
                    _deviceName,
                    loop: true,
                    lengthSec: 1,
                    frequency: (int)DefaultMicrophoneSampleRate
                );

                if (clip == null)
                    throw new InvalidOperationException("Microphone start failed");

                Debug.Log($"[QuestMicrophoneSource] Started new mic: {clip.channels}ch @ {clip.frequency}Hz");
            }

            var source = _sourceObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.volume = 1f; // MUST be > 0 to activate Android audio routing
            source.mute = false;
            source.playOnAwake = false;
            source.spatialBlend = 0f; // 2D audio
            source.priority = 0; // Highest priority
            
            Debug.Log($"[QuestMicrophoneSource] AudioSource configured - Volume: {source.volume}, Mute: {source.mute}, Spatial: {source.spatialBlend}, Priority: {source.priority}");

            var probeType = typeof(RtcAudioSource).Assembly.GetType("LiveKit.AudioProbe");
            var probe = _sourceObject.AddComponent(probeType) as MonoBehaviour;
            
            // DON'T clear after invocation - let RtcAudioSource read first
            // var clearMethod = probeType.GetMethod("ClearAfterInvocation");
            // clearMethod?.Invoke(probe, null);
            
            var audioReadEvent = probeType.GetEvent("AudioRead");
            var delegateType = audioReadEvent.EventHandlerType;
            var methodInfo = GetType().GetMethod("OnAudioRead", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var handler = Delegate.CreateDelegate(delegateType, this, methodInfo);
            audioReadEvent.AddEventHandler(probe, handler);
            
            Debug.Log($"[QuestMicrophoneSource] AudioProbe configured (ClearAfterInvocation: DISABLED for testing)");

            var waitUntilReady = new WaitUntil(() => Microphone.GetPosition(_deviceName) > 0);
            yield return waitUntilReady;
            source.Play();
            
            Debug.Log($"[QuestMicrophoneSource] AudioSource playing! Mic position: {Microphone.GetPosition(_deviceName)}");
            
            // Test: Read raw audio data from clip
            yield return new WaitForSeconds(0.5f);
            float[] testData = new float[clip.samples * clip.channels];
            clip.GetData(testData, 0);
            float testMax = 0f;
            for (int i = 0; i < Mathf.Min(testData.Length, 1000); i++)
            {
                testMax = Mathf.Max(testMax, Mathf.Abs(testData[i]));
            }
            Debug.Log($"[QuestMicrophoneSource] TEST: Raw AudioClip data max = {testMax:F4} (should be > 0 if mic is working)");
        }

        public override void Stop()
        {
            base.Stop();
            if (_coroutineHelper != null)
            {
                _coroutineHelper.StartCoroutine(StopMicrophone());
            }
            _started = false;
        }

        private IEnumerator StopMicrophone()
        {
            if (Microphone.IsRecording(_deviceName))
                Microphone.End(_deviceName);

            var probeType = typeof(RtcAudioSource).Assembly.GetType("LiveKit.AudioProbe");
            var probe = _sourceObject.GetComponent(probeType) as MonoBehaviour;
            if (probe != null)
            {
                var audioReadEvent = probeType.GetEvent("AudioRead");
                var delegateType = audioReadEvent.EventHandlerType;
                var methodInfo = GetType().GetMethod("OnAudioRead", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var handler = Delegate.CreateDelegate(delegateType, this, methodInfo);
                audioReadEvent.RemoveEventHandler(probe, handler);
                
                UnityEngine.Object.Destroy(probe);
            }

            var source = _sourceObject.GetComponent<AudioSource>();
            if (source != null)
                UnityEngine.Object.Destroy(source);

            if (_coroutineHelper != null)
                UnityEngine.Object.Destroy(_coroutineHelper);

            yield return null;
        }

        private void OnAudioRead(float[] data, int channels, int sampleRate)
        {
            AudioRead?.Invoke(data, channels, sampleRate);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Stop();
            base.Dispose(disposing);
        }
    }

    internal class CoroutineHelper : MonoBehaviour
    {
    }
}
