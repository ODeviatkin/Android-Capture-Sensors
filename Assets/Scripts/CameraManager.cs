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
    private ConcurrentQueue<(Texture2D frame, DateTime timestamp)> frameQueue = new ConcurrentQueue<(Texture2D, DateTime)>();
    private bool isCollectingFrames = false; // Initially set to false
    private int frameCount = 0;

    // Variables for FPS calculation
    private int framesThisSecond = 0;
    private float fpsTimer = 0f;

    [SerializeField] private GameObject CaptureButton;
    [SerializeField] private GameObject CapturingButton;
    [SerializeField] private GameObject SavingButton;

    void Start()
    {
        if (WebCamTexture.devices.Length > 0)
        {
            string cameraName = WebCamTexture.devices[0].name;

            // Initialize the WebCamTexture
            webCamTexture = new WebCamTexture(cameraName, 1920, 1080, numberOfFramesToCapture);
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
        // FPS Calculation
        fpsTimer += Time.deltaTime;
        if (fpsTimer >= 1f) // Every second
        {
            Debug.Log($"FPS: {framesThisSecond}");
            framesThisSecond = 0;
            fpsTimer = 0f;
        }

        if (webCamTexture != null && webCamTexture.isPlaying && isCollectingFrames)
        {
            if (webCamTexture.didUpdateThisFrame && frameQueue.Count < numberOfFramesToCapture)
            {
                framesThisSecond++;
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

                    int width = renderTexture.width;
                    int height = renderTexture.height;

                    // Get the data as Color32[]
                    var data = request.GetData<Color32>();

                    // Create an array to hold the processed pixels
                    Color32[] pixels = new Color32[data.Length];

                    // Process the pixels
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int index = y * width + x;
                            pixels[index] = data[index];
                        }
                    }

                    // Create the Texture2D with RGBA32 format
                    var capturedFrame = new Texture2D(width, height, TextureFormat.RGBA32, false);

                    // Set the pixels and apply
                    capturedFrame.SetPixels32(pixels);
                    capturedFrame.Apply();

                    frameQueue.Enqueue((capturedFrame, captureTime));
                });

                Debug.Log($"Frame {frameCount + 1}/{numberOfFramesToCapture} captured at {captureTime:HH:mm:ss.fff}");
                frameCount++;
            }
        }

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
        StopCameraFeed();

        while (frameQueue.TryDequeue(out var frameData))
        {
            yield return SaveFrameToGalleryCoroutine(frameData.frame, frameIndex, frameData.timestamp);
            Destroy(frameData.frame);
            frameIndex++;
        }
        Debug.Log("Finished saving all frames.");

        RestartCameraFeed();
        SavingButton.SetActive(false);
        CaptureButton.SetActive(true);
    }

    private IEnumerator SaveFrameToGalleryCoroutine(Texture2D frame, int index, DateTime timestamp)
    {
        if (frame == null)
        {
            Debug.LogError("Frame is null. Cannot save.");
            yield break;
        }

        string fileName = $"Frame_{index}_{timestamp:yyyyMMdd_HHmmss}.png";

        NativeGallery.Permission permission = NativeGallery.SaveImageToGallery(
            frame,
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

        yield return null;
    }

    public void StopCameraFeed()
    {
        Debug.Log("Stopping camera feed...");
        if (webCamTexture != null && webCamTexture.isPlaying)
        {
            webCamTexture.Stop();
            cameraView.texture = null; // Remove the feed from RawImage
        }
    }

    public void RestartCameraFeed()
    {
        Debug.Log("Restarting camera feed...");
        if (webCamTexture != null)
        {
            webCamTexture.Play();
            cameraView.texture = webCamTexture; // Reassign the feed to RawImage
        }
    }

    void OnDestroy()
    {
        if (webCamTexture != null)
        {
            webCamTexture.Stop();
        }

        if (renderTexture != null)
        {
            renderTexture.Release();
        }
    }
}