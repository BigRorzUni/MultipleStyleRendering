using UnityEngine;
using UnityEngine.InputSystem;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Rendering.Universal;
using Unity.VisualScripting;
using System;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 5f;
    public float sprintSpeed = 8f;
    public float jumpHeight = 1.5f;
    public float gravity = -9.81f;

    [Header("Look")]
    public Camera playerCamera;
    public float mouseSensitivity = 2f;
    public float maxLookAngle = 85f;
    [SerializeField] bool mouseLocked = true;

    CharacterController controller;

    float verticalVelocity;
    float pitch;

    NprStylesRendererFeature feature;
    [SerializeField] UI ui;

    [Header("Style Selection")]
    [SerializeField] List<StyleOption> availableStyles = new()
    {
        new StyleOption
        {
            displayName = "Outlines",
            imageEffect = StyleBits.ImageSpaceEffect.Outline,
        },
        new StyleOption
        {
            displayName = "Dithering",
            imageEffect = StyleBits.ImageSpaceEffect.Dithering,
        },
        new StyleOption
        {
            displayName = "Greyscale",
            imageEffect = StyleBits.ImageSpaceEffect.Greyscale,
        }
    };

    int currentStyleIndex = 0;

    void Start()
    {
        controller = GetComponent<CharacterController>();

        if (playerCamera == null)
            playerCamera = GetComponentInChildren<Camera>();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        FindRendererFeature();

        StartCoroutine(InitUI());

        UpdateStyleUI();

    }

    void UpdateStyleUI()
    {
        if (ui == null || availableStyles.Count == 0)
            return;

        ui.SetStyle(availableStyles[currentStyleIndex].displayName);
    }

    IEnumerator InitUI()
    {
        // wait 1 frame to ensure UI Awake() has run
        yield return null;

        if (ui == null)
        {
            Debug.LogWarning("UI reference not assigned");
            yield break;
        }

        ui.SetMode(NprConfig.RenderMode.ToString());
    }

    void FindRendererFeature()
    {

        var renderer = (ScriptableRenderer)UniversalRenderPipeline.asset.GetRenderer(0);
        var field = typeof(ScriptableRenderer).GetField("m_RendererFeatures", BindingFlags.NonPublic | BindingFlags.Instance);
        var list = field.GetValue(renderer) as IList;

        foreach (var f in list)
        {
            if (f is NprStylesRendererFeature featureFound)
            {
                feature = featureFound;
                Debug.Log("Found NPR Renderer Feature");
                return;
            }
        }
        Debug.LogError("NPR Renderer Feature not found");
    }


    void Update()
    {
        SwitchMode();

        if(mouseLocked)
            Look();

        Move();

        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            mouseLocked = !mouseLocked;

            Cursor.lockState = mouseLocked ? CursorLockMode.Locked : CursorLockMode.None;

            Cursor.visible = !mouseLocked;
        }

        HandleStyleSelection();

        HandleStyleRaycast();
    }

    void HandleStyleSelection()
    {
        if (availableStyles.Count == 0)
            return;

        if (Keyboard.current.qKey.wasPressedThisFrame)
        {
            currentStyleIndex--;

            if (currentStyleIndex < 0)
                currentStyleIndex = availableStyles.Count - 1;

            UpdateStyleUI();
        }

        if (Keyboard.current.eKey.wasPressedThisFrame)
        {
            currentStyleIndex++;

            if (currentStyleIndex >= availableStyles.Count)
                currentStyleIndex = 0;

            UpdateStyleUI();
        }
    }

    void SwitchMode()
    {
        if (Keyboard.current.digit1Key.wasPressedThisFrame)
        {
            SetMode(NprRenderMode.Fullscreen);
        }

        if (Keyboard.current.digit2Key.wasPressedThisFrame)
        {
            SetMode(NprRenderMode.CPU);
        }

        if (Keyboard.current.digit3Key.wasPressedThisFrame)
        {
            SetMode(NprRenderMode.GPU);
        }

        if (Keyboard.current.digit4Key.wasPressedThisFrame)
        {
            SetMode(NprRenderMode.Tiling);
        }

        if (Keyboard.current.tabKey.wasPressedThisFrame)
        {
            NprConfig.DebugBBoxes = !NprConfig.DebugBBoxes;
        }
    }

    void SetMode(NprRenderMode mode)
    {
        if (feature == null)
            return;

        if (feature.settings.renderMode == mode)
            return;

        feature.settings.renderMode = mode;

        ui.SetMode(mode.ToString());

        Debug.Log($"Switched mode to {mode}");

        feature.Create();
    }


    void HandleStyleRaycast()
    {
        if (Mouse.current == null)
            return;

        if (!Mouse.current.leftButton.wasPressedThisFrame && !Mouse.current.rightButton.wasPressedThisFrame)
            return;

        Ray ray = playerCamera.ViewportPointToRay( new Vector3(0.5f, 0.5f, 0f) );

        if (!Physics.Raycast(ray, out RaycastHit hit))
            return;

        StylisedTag tag = hit.collider.GetComponentInParent<StylisedTag>();

        if (tag == null)
            return;

        if (Mouse.current.leftButton.wasPressedThisFrame)
            AddCurrentStyle(tag);

        if (Mouse.current.rightButton.wasPressedThisFrame)
            ClearStyles(tag);
    }

    void AddCurrentStyle(StylisedTag tag)
    {
        StyleOption style = availableStyles[currentStyleIndex];

        tag.imageEffects |= style.imageEffect;

        tag.Apply();

        Debug.Log($"Added {style.displayName}");
    }

    void ClearStyles(StylisedTag tag)
    {
        tag.imageEffects = StyleBits.ImageSpaceEffect.None;

        tag.Apply();

        Debug.Log("Cleared styles");
    }

    void Look()
    {
        Vector2 mouseDelta = Mouse.current.delta.ReadValue();


        float mouseX = mouseDelta.x;
        float mouseY = mouseDelta.y;

        transform.Rotate(Vector3.up * mouseX);

        pitch -= mouseY;
        pitch = Mathf.Clamp(pitch, -maxLookAngle, maxLookAngle);

        playerCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    void Move()
    {
        Vector2 moveInput = Vector2.zero;

        if (Keyboard.current.wKey.isPressed) 
            moveInput.y += 1;
        if (Keyboard.current.sKey.isPressed) 
            moveInput.y -= 1;
        if (Keyboard.current.dKey.isPressed) 
            moveInput.x += 1;
        if (Keyboard.current.aKey.isPressed) 
            moveInput.x -= 1;

        float x = moveInput.x;
        float z = moveInput.y;

        Vector3 move = transform.right * x + transform.forward * z;

        float speed = Keyboard.current.leftShiftKey.isPressed ? sprintSpeed : moveSpeed;

        if (controller.isGrounded)
        {
            if (verticalVelocity < 0)
                verticalVelocity = -2f;

            if (Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }
        }

        verticalVelocity += gravity * Time.deltaTime;

        move *= speed;
        move.y = verticalVelocity;

        controller.Move(move * Time.deltaTime);
    }
}