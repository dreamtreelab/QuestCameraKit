using UnityEngine;

public class RendOffInChildObjects : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        DisableChildRenderers();
    }

    void DisableChildRenderers()
    {
        // Get all Renderer components in this object and its children
        Renderer[] allRenderers = GetComponentsInChildren<Renderer>();

        foreach (Renderer rend in allRenderers)
        {
            // Optional: Check if the renderer is on the parent itself 
            // if you only want to disable TRUE children.
            if (rend.gameObject != this.gameObject)
            {
                rend.enabled = false;
            }
            
            // If you want to disable EVERYTHING (including this object), 
            // just use: rend.enabled = false;
        }
    }
}