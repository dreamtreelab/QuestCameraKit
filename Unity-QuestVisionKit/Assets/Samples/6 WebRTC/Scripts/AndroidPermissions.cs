using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace QuestCameraKit.WebRTC
{
    public class AndroidPermissions : MonoBehaviour
    {
        [SerializeField] private float recheckInterval = 2f;
        private float nextCheckTime = 0f;

        private void Start()
        {
#if UNITY_ANDROID
            Debug.Log("[AndroidPermissions] Checking permissions on Start...");
            RequestPermissions();
#endif
        }

        private void Update()
        {
#if UNITY_ANDROID
            if (Time.time >= nextCheckTime)
            {
                nextCheckTime = Time.time + recheckInterval;
                CheckPermissionStatus();
            }
#endif
        }

#if UNITY_ANDROID
        private void RequestPermissions()
        {
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                Debug.Log("[AndroidPermissions] Requesting microphone permission...");
                Permission.RequestUserPermission(Permission.Microphone);
            }
            else
            {
                Debug.Log("[AndroidPermissions] Microphone permission already granted");
            }
            
            if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
            {
                Debug.Log("[AndroidPermissions] Requesting camera permission...");
                Permission.RequestUserPermission(Permission.Camera);
            }
            else
            {
                Debug.Log("[AndroidPermissions] Camera permission already granted");
            }
        }

        private void CheckPermissionStatus()
        {
            bool hasMic = Permission.HasUserAuthorizedPermission(Permission.Microphone);
            bool hasCam = Permission.HasUserAuthorizedPermission(Permission.Camera);
            
            if (!hasMic || !hasCam)
            {
                Debug.LogWarning($"[AndroidPermissions] Missing permissions - Mic: {hasMic}, Cam: {hasCam}");
                
                // Try requesting again
                if (!hasMic)
                {
                    Permission.RequestUserPermission(Permission.Microphone);
                }
                if (!hasCam)
                {
                    Permission.RequestUserPermission(Permission.Camera);
                }
            }
        }
#endif
    }
}
