using Unity.Collections;
using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections;
using System.Collections.Concurrent;
using UnityEngine.Rendering;

public class CameraManager : MonoBehaviour
{
    public RawImage cameraView; // UI element to display the camera feed
    public int numberOfFramesToCapture = 60; // Number of frames to capture

    private WebCamTexture webCamTexture;
    private RenderTexture renderTexture;
    private ConcurrentQueue<(byte[] rawData, DateTime timestamp)> frameQueue = new ConcurrentQueue<(byte[], DateTime)>();
    private bool isCollectingFrames = false; // Initially set to false
    private int frameCount = 0;

    [SerializeField] private GameObject CaptureButton;
    [SerializeField] private GameObject CapturingButton;
    [SerializeField] private GameObject SavingButton;

    void Start()
    {
        if (WebCamTexture.devices.Length > 0)
        {
            string cameraName = WebCamTexture.devices[0].name;

            // Initialize the WebCamTexture
            webCamTexture = new WebCamTexture(cameraName, 1920, 1080, 30);
            renderTexture = new RenderTexture(1920, 1080, 0, RenderTextureFormat.ARGB32);

            // Assign the camera feed to RawImage for display
            if (cameraView != null)
            {
                cameraView.texture = webCamTexture;
            }

            // Start the camera
            webCamTexture.Play();
            StartCoroutine(WaitForCameraToStart());
        }
        else
        {
            Debug.LogError("No camera available!");
        }
    }

    IEnumerator WaitForCameraToStart()
    {
        while (webCamTexture != null && (webCamTexture.width < 100 || webCamTexture.height < 100))
        {
            Debug.Log("Waiting for camera to initialize...");
            yield return null;
        }
        Debug.Log($"Camera initialized: {webCamTexture.width} x {webCamTexture.height}");
    }

    void Update()
    {
        if (webCamTexture != null && webCamTexture.isPlaying && isCollectingFrames)
        {
            if (webCamTexture.didUpdateThisFrame && frameQueue.Count < numberOfFramesToCapture)
            {
                DateTime captureTime = DateTime.Now;

                // Copy the WebCamTexture to a RenderTexture
                Graphics.Blit(webCamTexture, renderTexture);

                // Use AsyncGPUReadback to capture the frame asynchronously
                AsyncGPUReadback.Request(renderTexture, 0, request =>
                {
                    if (request.hasError)
                    {
                        Debug.LogError("Error during AsyncGPUReadback");
                        return;
                    }

                    // Get raw byte data from GPU memory
                    NativeArray<byte> rawData = request.GetData<byte>();

                    // Copy raw data to a byte array for saving
                    byte[] rawCopy = new byte[rawData.Length];
                    rawData.CopyTo(rawCopy);

                    frameQueue.Enqueue((rawCopy, captureTime));
                    Debug.Log($"Frame {frameQueue.Count}/{numberOfFramesToCapture} captured at {captureTime:HH:mm:ss.fff}");
                });

                frameCount++;
            }
        }

        // Stop collecting frames if we reach the limit
        if (isCollectingFrames && frameQueue.Count >= numberOfFramesToCapture)
        {
            Debug.Log($"Collected {numberOfFramesToCapture} frames. Stopping collection.");
            isCollectingFrames = false;
            StartCoroutine(SaveFramesAndStop());
        }
    }

    public void StartRecordingFrames()
    {
        Debug.Log("Recording started!");
        CaptureButton.SetActive(false);
        CapturingButton.SetActive(true);
        isCollectingFrames = true;
    }

    private IEnumerator SaveFramesAndStop()
    {
        Debug.Log($"Saving {numberOfFramesToCapture} frames to gallery...");
        int frameIndex = 0;
        CapturingButton.SetActive(false);
        SavingButton.SetActive(true);

        while (frameQueue.TryDequeue(out var frameData))
        {
            yield return SaveFrameToGalleryCoroutine(frameData.rawData, renderTexture.width, renderTexture.height, frameIndex, frameData.timestamp);
            frameIndex++;
        }

        Debug.Log("Finished saving all frames.");
        SavingButton.SetActive(false);
        CaptureButton.SetActive(true);
    }

    private IEnumerator SaveFrameToGalleryCoroutine(byte[] rawData, int width, int height, int index, DateTime timestamp)
    {
        // Create Texture2D for saving
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.LoadRawTextureData(rawData);
        texture.Apply();

        // Rotate the frame if needed (optional)
        var rotatedTexture = RotateTexture(texture, 90f);

        // Save to gallery
        string fileName = $"Frame_{index}_{timestamp:yyyyMMdd_HHmmss}.png";
        NativeGallery.Permission permission = NativeGallery.SaveImageToGallery(
            rotatedTexture,
            "VOJO",
            fileName,
            (success, path) =>
            {
                if (success)
                {
                    Debug.Log($"Frame {index} saved to Pictures at {path}");
                }
                else
                {
                    Debug.LogError("Failed to save frame to the gallery.");
                }
            }
        );

        if (permission != NativeGallery.Permission.Granted)
        {
            Debug.LogError("Gallery permission not granted. Cannot save frame.");
        }

        Destroy(texture);
        Destroy(rotatedTexture);

        yield return null;
    }

    private Texture2D RotateTexture(Texture2D originalTexture, float angle)
    {
        int width = originalTexture.width;
        int height = originalTexture.height;

        Texture2D rotatedTexture = new Texture2D(height, width, originalTexture.format, false);

        Color32[] originalPixels = originalTexture.GetPixels32();
        Color32[] rotatedPixels = new Color32[originalPixels.Length];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int originalIndex = y * width + x;
                int rotatedIndex;

                // Apply 90-degree rotation logic (clockwise)
                rotatedIndex = (width - x - 1) * height + y;
                rotatedPixels[rotatedIndex] = originalPixels[originalIndex];
            }
        }

        rotatedTexture.SetPixels32(rotatedPixels);
        rotatedTexture.Apply();

        return rotatedTexture;
    }
}
