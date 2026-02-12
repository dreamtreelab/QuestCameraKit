using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
using NativeWebSocket;
using LiveKit;

namespace QuestCameraKit.WebRTC
{
    public class UltravoxLiveKitClient : MonoBehaviour
    {
        [Header("Ultravox Configuration")]
        [SerializeField] private string agentId = "d591fcc0-0ba1-4c44-a557-50ddbb2aad2e";
        [SerializeField] private string apiKey = "XchZ5NFm.wbPl5VdmMmjfZIvjuMDMo4q7qyo8XwJg";
        [SerializeField] private string ultravoxApiUrl = "https://api.ultravox.ai/api/agents";
        
        [Header("Connection Settings")]
        [SerializeField] private float startupDelay = 5f;
        
        [Header("Audio")]
        [SerializeField] private GameObject microphoneSourceObject;
        [SerializeField] private AudioSource agentAudioSource;
        
        [Header("Debug")]
        [SerializeField] private TextMeshPro debugText;
        [SerializeField] private Text statusText;
        [SerializeField] private int maxDebugLines = 25;
        
        private Room livekitRoom;
        private WebSocket signalingWebSocket;
        private LocalAudioTrack localAudioTrack;
        private RtcAudioSource microphoneSource;
        
        private string currentCallId;
        private string joinUrl;
        private string roomUrl;
        private string roomToken;
        private bool isConnected = false;
        private bool isConnecting = false;
        private bool isMuted = false;
        
        private List<string> debugBuffer = new List<string>();
        
        [Serializable]
        private class CreateCallRequest
        {
            public MediumSettings medium;
        }
        
        [Serializable]
        private class MediumSettings
        {
            public WebRtcSettings webRtc = new WebRtcSettings();
        }
        
        [Serializable]
        private class WebRtcSettings
        {
        }
        
        [Serializable]
        private class CreateCallResponse
        {
            public string callId;
            public string joinUrl;
        }
        
        [Serializable]
        private class RoomInfoMessage
        {
            public string type;
            public string roomUrl;
            public string token;
        }
        
        [Serializable]
        private class DataMessage
        {
            public string type;
            public string message;
        }
        
        private void Start()
        {
            LogMessage("=== Ultravox LiveKit Client Started ===");
            LogMessage($"Unity Version: {Application.unityVersion}");
            LogMessage($"Platform: {Application.platform}");
            
            CheckPermissions();
            
            StartCoroutine(DelayedStart());
        }
        
        private void CheckPermissions()
        {
#if UNITY_ANDROID
            LogMessage("Checking Android permissions...");
            
            bool hasMic = UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone);
            bool hasCam = UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera);
            
            LogMessage($"Microphone Permission: {(hasMic ? "GRANTED" : "DENIED")}");
            LogMessage($"Camera Permission: {(hasCam ? "GRANTED" : "DENIED")}");
            
            if (!hasMic)
            {
                LogMessage("Requesting microphone permission...");
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
            }
            
            if (!hasCam)
            {
                LogMessage("Requesting camera permission...");
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera);
            }
#endif
        }
        
        private IEnumerator DelayedStart()
        {
            LogMessage($"Waiting {startupDelay} seconds before starting call...");
            UpdateStatus($"Starting in {startupDelay}s...");
            yield return new WaitForSeconds(startupDelay);
            
            StartCoroutine(CreateAndJoinCall());
        }
        
        private IEnumerator CreateAndJoinCall()
        {
            isConnecting = true;
            LogMessage("Creating Ultravox call...");
            UpdateStatus("Creating call...");
            
            string createCallUrl = $"{ultravoxApiUrl}/{agentId}/calls";
            
            CreateCallRequest requestBody = new CreateCallRequest
            {
                medium = new MediumSettings()
            };
            
            string jsonBody = JsonUtility.ToJson(requestBody);
            LogMessage($"POST {createCallUrl}");
            
            using (UnityWebRequest request = new UnityWebRequest(createCallUrl, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("X-API-Key", apiKey);
                request.timeout = 10;
                
                yield return request.SendWebRequest();
                
                if (request.result != UnityWebRequest.Result.Success)
                {
                    LogError($"Failed to create call: {request.error}");
                    LogError($"Response Code: {request.responseCode}");
                    LogError($"Response: {request.downloadHandler?.text ?? "null"}");
                    UpdateStatus("Failed to create call");
                    isConnecting = false;
                    yield break;
                }
                
                string responseText = request.downloadHandler.text;
                LogMessage($"✅ Call created successfully");
                
                CreateCallResponse response = JsonUtility.FromJson<CreateCallResponse>(responseText);
                currentCallId = response.callId;
                joinUrl = response.joinUrl;
                
                LogMessage($"Call ID: {currentCallId}");
                LogMessage($"Join URL: {joinUrl}");
            }
            
            yield return StartCoroutine(ConnectToUltravoxSignaling());
        }
        
        private IEnumerator ConnectToUltravoxSignaling()
        {
            UpdateStatus("Connecting to Ultravox...");
            LogMessage("Initializing WebSocket signaling...");
            
            signalingWebSocket = new WebSocket(joinUrl);
            
            signalingWebSocket.OnOpen += () =>
            {
                LogMessage("✅ WebSocket connected to Ultravox!");
            };
            
            signalingWebSocket.OnError += (e) =>
            {
                LogError($"WebSocket error: {e}");
            };
            
            signalingWebSocket.OnClose += (e) =>
            {
                LogMessage($"WebSocket closed: {e}");
                isConnected = false;
            };
            
            signalingWebSocket.OnMessage += (bytes) =>
            {
                string message = Encoding.UTF8.GetString(bytes);
                HandleSignalingMessage(message);
            };
            
            yield return signalingWebSocket.Connect();
            
            while (signalingWebSocket.State == WebSocketState.Connecting)
            {
                yield return null;
            }
            
            if (signalingWebSocket.State != WebSocketState.Open)
            {
                LogError($"Failed to connect WebSocket. State: {signalingWebSocket.State}");
                isConnecting = false;
            }
        }
        
        private void HandleSignalingMessage(string message)
        {
            LogMessage($"Received: {message.Substring(0, Mathf.Min(100, message.Length))}...");
            
            try
            {
                RoomInfoMessage roomInfo = JsonUtility.FromJson<RoomInfoMessage>(message);
                
                if (roomInfo.type == "room_info")
                {
                    LogMessage("✅ Received room_info from Ultravox");
                    roomUrl = roomInfo.roomUrl;
                    roomToken = roomInfo.token;
                    
                    LogMessage($"Room URL: {roomUrl}");
                    LogMessage($"Token: {roomToken.Substring(0, Mathf.Min(20, roomToken.Length))}...");
                    
                    StartCoroutine(ConnectToLiveKitRoom());
                }
                else
                {
                    LogMessage($"Received message type: {roomInfo.type}");
                }
            }
            catch (System.Exception ex)
            {
                LogError($"Failed to parse signaling message: {ex.Message}");
            }
        }
        
        private IEnumerator ConnectToLiveKitRoom()
        {
            LogMessage("Connecting to LiveKit room...");
            UpdateStatus("Connecting to LiveKit...");
            
            livekitRoom = new Room();
            
            livekitRoom.TrackSubscribed += OnTrackSubscribed;
            livekitRoom.ParticipantConnected += OnParticipantConnected;
            livekitRoom.Connected += OnRoomConnected;
            livekitRoom.Disconnected += OnRoomDisconnected;
            
            RoomOptions options = new RoomOptions
            {
                AutoSubscribe = true,
                Dynacast = false,
                AdaptiveStream = false
            };
            
            var connectInstruction = livekitRoom.Connect(roomUrl, roomToken, options);
            
            while (!connectInstruction.IsDone)
            {
                yield return null;
            }
            
            if (connectInstruction.IsError)
            {
                LogError($"Failed to connect to LiveKit room");
                UpdateStatus("Connection failed");
                isConnecting = false;
                yield break;
            }
            
            LogMessage("✅ Connected to LiveKit room!");
            
            yield return StartCoroutine(PublishMicrophone());
        }
        
        private IEnumerator PublishMicrophone()
        {
            LogMessage("Setting up microphone...");
            
            if (microphoneSourceObject == null)
            {
                LogError("Microphone Source Object not assigned!");
                yield break;
            }
            
            // Check microphone permission first
            if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
            {
                LogMessage("Requesting microphone permission...");
                yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);
                
                if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
                {
                    LogError("Microphone permission denied!");
                    yield break;
                }
            }
            
            LogMessage("Microphone permission granted");
            
            // Log all available microphone devices
            if (Microphone.devices.Length == 0)
            {
                LogError("No microphone devices found!");
                yield break;
            }
            
            LogMessage($"Found {Microphone.devices.Length} microphone device(s):");
            for (int i = 0; i < Microphone.devices.Length; i++)
            {
                LogMessage($"  [{i}] {Microphone.devices[i]}");
            }
            
            string deviceName = Microphone.devices[0];
            LogMessage($"Using device [0]: {deviceName}");
            
            if (string.IsNullOrEmpty(deviceName))
            {
                LogError("Selected device name is empty!");
                yield break;
            }
            
            // Detect microphone format by testing
            LogMessage("Detecting microphone format...");
            AudioClip testClip = Microphone.Start(deviceName, true, 1, 48000);
            
            if (testClip == null)
            {
                LogError("Failed to start microphone for testing!");
                yield break;
            }
            
            yield return new WaitForSeconds(0.2f);
            
            int actualChannels = testClip.channels;
            int actualFrequency = testClip.frequency;
            LogMessage($"✅ Detected mic: {actualChannels}ch @ {actualFrequency}Hz");
            
            // IMPORTANT: Stop the test microphone
            Microphone.End(deviceName);
            LogMessage("Test microphone stopped");
            yield return new WaitForSeconds(0.1f);
            
            // Create QuestMicrophoneSource - it will start mic fresh with matching parameters
            microphoneSource = new QuestMicrophoneSource(deviceName, microphoneSourceObject, actualChannels, null);
            LogMessage($"QuestMicrophoneSource created with {actualChannels} channels (will start fresh)");
            
            // Subscribe to audio capture events BEFORE starting
            // IMPORTANT: AudioRead callback runs on audio thread - don't call LogMessage!
            int audioReadCount = 0;
            float lastMaxSample = 0f;
            microphoneSource.AudioRead += (data, channels, sampleRate) =>
            {
                audioReadCount++;
                
                // Calculate max sample (on audio thread is fine)
                float maxSample = 0f;
                for (int i = 0; i < Mathf.Min(data.Length, 1000); i++)
                {
                    maxSample = Mathf.Max(maxSample, Mathf.Abs(data[i]));
                }
                lastMaxSample = maxSample;
                
                // Only use Debug.Log (thread-safe), not LogMessage (touches UI)
                if (audioReadCount % 50 == 0)
                {
                    Debug.Log($"[UltravoxLiveKitClient] 🎤 Audio #{audioReadCount}: max={maxSample:F4}, rate={sampleRate}, ch={channels}");
                }
            };
            LogMessage("AudioRead event subscribed");
            
            // Start the microphone source (will start fresh)
            microphoneSource.Start();
            LogMessage("QuestMicrophoneSource.Start() called");
            
            // Wait a moment for microphone to initialize
            yield return new WaitForSeconds(1.0f);
            
            // Verify AudioSource was added
            AudioSource addedSource = microphoneSourceObject.GetComponent<AudioSource>();
            if (addedSource != null)
            {
                LogMessage($"✅ AudioSource added (clip: {(addedSource.clip != null ? addedSource.clip.name : "null")})");
                if (addedSource.clip != null && Microphone.IsRecording(deviceName))
                {
                    int pos = Microphone.GetPosition(deviceName);
                    LogMessage($"✅ Microphone IS recording! Position: {pos}");
                    
                    // Check if AudioProbe was added
                    var audioProbe = microphoneSourceObject.GetComponent("AudioProbe");
                    if (audioProbe != null)
                    {
                        LogMessage("✅ AudioProbe component added");
                    }
                    else
                    {
                        LogError("❌ AudioProbe component NOT found!");
                    }
                }
                else
                {
                    LogError($"❌ Microphone NOT recording! IsRecording: {Microphone.IsRecording(deviceName)}");
                }
            }
            else
            {
                LogError("❌ AudioSource NOT added - MicrophoneSource failed to initialize!");
                yield break;
            }
            
            localAudioTrack = LocalAudioTrack.CreateAudioTrack("microphone", microphoneSource, livekitRoom);
            LogMessage("✅ Audio track created");
            
            var publishOptions = new LiveKit.Proto.TrackPublishOptions();
            var publishInstruction = livekitRoom.LocalParticipant.PublishTrack(localAudioTrack, publishOptions);
            
            yield return publishInstruction;
            
            if (publishInstruction.IsError)
            {
                LogError($"Failed to publish audio track");
                yield break;
            }
            
            LogMessage("✅ Microphone published!");
            
            // Log final status
            LogMessage($"Track - Muted: {localAudioTrack.Muted}, Kind: {localAudioTrack.Kind}, Sid: {localAudioTrack.Sid}");
            
            // Wait a bit and check if audio is still being captured
            yield return new WaitForSeconds(3.0f);
            LogMessage($"📊 Audio capture summary: Total reads = {audioReadCount}, Last max = {lastMaxSample:F4}");
            
            if (audioReadCount == 0)
            {
                LogError("❌ CRITICAL: No audio data captured! AudioRead event never fired!");
                LogError("This means AudioProbe is not working or audio pipeline is broken.");
            }
            else if (lastMaxSample < 0.01f)
            {
                LogError($"⚠️ WARNING: Audio captured but SILENT! Max level = {lastMaxSample:F4}");
                LogError("Possible causes:");
                LogError("1. Quest microphone muted in system settings");
                LogError("2. Unity AudioListener stealing mic input");
                LogError("3. AudioProbe clearing before RtcAudioSource reads");
                LogError("4. Android audio focus not granted");
            }
            else if (lastMaxSample < 0.05f)
            {
                LogError($"⚠️ WARNING: Audio very quiet! Max level = {lastMaxSample:F4}");
                LogError("Speak louder or check Quest microphone settings");
            }
            else
            {
                LogMessage($"✅ Audio capture working! Level = {lastMaxSample:F4}");
            }
        }
        
        private void OnRoomConnected(Room room)
        {
            LogMessage("✅ LiveKit Room Connected!");
            UpdateStatus("Connected! Talk to agent...");
            isConnected = true;
            isConnecting = false;
        }
        
        private void OnRoomDisconnected(Room room)
        {
            LogMessage("LiveKit Room Disconnected");
            UpdateStatus("Disconnected");
            isConnected = false;
        }
        
        private void OnParticipantConnected(Participant participant)
        {
            LogMessage($"Participant connected: {participant.Identity}");
        }
        
        private void OnTrackSubscribed(IRemoteTrack track, RemoteTrackPublication publication, RemoteParticipant participant)
        {
            LogMessage($"✅ Track subscribed: {track.Kind} from {participant.Identity}");
            
            if (track is RemoteAudioTrack remoteAudioTrack)
            {
                LogMessage("Setting up agent audio playback...");
                UpdateStatus("Agent audio received!");
                
                try
                {
                    StartCoroutine(PlayRemoteAudio(remoteAudioTrack));
                }
                catch (System.Exception ex)
                {
                    LogError($"Failed to set up audio playback: {ex.Message}");
                }
            }
        }
        
        private IEnumerator PlayRemoteAudio(RemoteAudioTrack remoteTrack)
        {
            LogMessage("Starting remote audio playback...");
            
            if (agentAudioSource == null)
            {
                LogError("Agent AudioSource not assigned!");
                yield break;
            }
            
            AudioStream audioStream = new AudioStream(remoteTrack, agentAudioSource);
            
            LogMessage("✅ Agent audio stream created and playing!");
            
            yield return null;
        }
        
        private void Update()
        {
            if (signalingWebSocket != null)
            {
#if !UNITY_WEBGL || UNITY_EDITOR
                signalingWebSocket.DispatchMessageQueue();
#endif
            }
            
            if (isConnected)
            {
                if (OVRInput.GetDown(OVRInput.Button.Two) || 
                    OVRInput.GetDown(OVRInput.Button.Four))
                {
                    ToggleMute();
                }
            }
            
#if UNITY_EDITOR
            if (isConnected)
            {
                if (Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame)
                {
                    ToggleMute();
                }
            }
#endif
        }
        
        private void ToggleMute()
        {
            isMuted = !isMuted;
            
            if (localAudioTrack != null)
            {
                (localAudioTrack as ILocalTrack).SetMute(isMuted);
            }
            
            string status = isMuted ? "MUTED" : "UNMUTED";
            LogMessage($"🎤 Microphone {status}");
            UpdateStatus(isMuted ? "Muted" : "Connected");
        }
        
        private void OnApplicationQuit()
        {
            LogMessage("Application quitting - terminating call...");
            StartCoroutine(TerminateCall());
        }
        
        private IEnumerator TerminateCall()
        {
            if (string.IsNullOrEmpty(currentCallId))
            {
                LogMessage("No active call to terminate");
                yield break;
            }
            
            LogMessage("Sending hang up message...");
            
            string endpoint = $"{ultravoxApiUrl.Replace("/agents", "")}/calls/{currentCallId}/send_data_message";
            
            DataMessage hangUpMessage = new DataMessage
            {
                type = "hang_up",
                message = "Goodbye!"
            };
            
            string jsonBody = JsonUtility.ToJson(hangUpMessage);
            
            using (UnityWebRequest request = new UnityWebRequest(endpoint, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("X-API-Key", apiKey);
                request.timeout = 5;
                
                yield return request.SendWebRequest();
                
                if (request.result == UnityWebRequest.Result.Success)
                {
                    LogMessage("✅ Call terminated successfully");
                }
                else
                {
                    LogError($"Failed to terminate call: {request.error}");
                }
            }
            
            CleanupResources();
        }
        
        private void CleanupResources()
        {
            if (signalingWebSocket != null && signalingWebSocket.State == WebSocketState.Open)
            {
                signalingWebSocket.Close();
            }
            
            if (livekitRoom != null)
            {
                try
                {
                    livekitRoom.Disconnect();
                    livekitRoom = null;
                }
                catch (System.Exception ex)
                {
                    LogError($"Error disconnecting from LiveKit: {ex.Message}");
                }
            }
            
            if (microphoneSource != null)
            {
                try
                {
                    microphoneSource.Stop();
                    microphoneSource.Dispose();
                    microphoneSource = null;
                }
                catch (System.Exception ex)
                {
                    LogError($"Error disposing microphone source: {ex.Message}");
                }
            }
            
            LogMessage("Resources cleaned up");
        }
        
        private void OnDestroy()
        {
            CleanupResources();
        }
        
        private void LogMessage(string message)
        {
            string timestamp = System.DateTime.Now.ToString("HH:mm:ss");
            string logEntry = $"[{timestamp}] {message}";
            
            Debug.Log($"[UltravoxLiveKitClient] {message}");
            
            debugBuffer.Add(logEntry);
            if (debugBuffer.Count > maxDebugLines)
            {
                debugBuffer.RemoveAt(0);
            }
            
            UpdateDebugText();
        }
        
        private void LogError(string message)
        {
            string timestamp = System.DateTime.Now.ToString("HH:mm:ss");
            string logEntry = $"<color=red>[{timestamp}] ERROR: {message}</color>";
            
            Debug.LogError($"[UltravoxLiveKitClient] {message}");
            
            debugBuffer.Add(logEntry);
            if (debugBuffer.Count > maxDebugLines)
            {
                debugBuffer.RemoveAt(0);
            }
            
            UpdateDebugText();
        }
        
        private void UpdateDebugText()
        {
            if (debugText != null)
            {
                debugText.text = string.Join("\n", debugBuffer);
            }
        }
        
        private void UpdateStatus(string status)
        {
            if (statusText != null)
            {
                statusText.text = status;
            }
        }
    }
}
