using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

[RequireComponent(typeof(Button))]
public class UIButtonEffects : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, IPointerUpHandler
{
    [Header("Visual References")]
    [SerializeField] private Image backgroundImage;
    [SerializeField] private TextMeshProUGUI buttonText;

    [Header("Background Colors")]
    // [SerializeField] private Color normalBackgroundColor = Color.white;
    // [SerializeField] private Color hoverBackgroundColor = new Color(1f, 0.8f, 0.25f, 1f);

    [Header("Text Colors")]
    [SerializeField] private Color normalTextColor = Color.black;
    [SerializeField] private Color hoverTextColor = Color.white;

    [Header("Scale Animation")]
    [SerializeField, Min(1f)] private float hoverScaleMultiplier = 1.08f;
    [SerializeField, Range(0.5f, 1f)] private float pressedScaleMultiplier = 0.94f;
    [SerializeField, Min(0.01f)] private float scaleDuration = 0.12f;
    [SerializeField, Min(0.01f)] private float colorDuration = 0.12f;

    [Header("Sound Effects")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip hoverSound;
    [SerializeField] private AudioClip clickSound;
    [SerializeField, Range(0f, 1f)] private float soundVolume = 1f;

    private Button button;
    private Vector3 originalScale;
    private Coroutine scaleCoroutine;
    private Coroutine colorCoroutine;
    private bool isHovered;

    private void Awake()
    {
        button = GetComponent<Button>();
        originalScale = transform.localScale;

        // Prevent Unity's built-in Button tint from conflicting with custom colors.
        // button.transition = Selectable.Transition.None;

        if (backgroundImage == null)
            backgroundImage = button.targetGraphic as Image;

        ApplyColors(false);
        transform.localScale = originalScale;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!button.interactable) return;

        isHovered = true;
        PlaySound(hoverSound);
        AnimateColors(hoverTextColor);
        AnimateScale(originalScale * hoverScaleMultiplier);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovered = false;
        AnimateColors(normalTextColor);
        AnimateScale(originalScale);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!button.interactable) return;

        AnimateColors(normalTextColor);
        PlaySound(clickSound);
        AnimateScale(originalScale * pressedScaleMultiplier);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        AnimateScale(isHovered
            ? originalScale * hoverScaleMultiplier
            : originalScale);
    }

    private void AnimateColors(Color targetText)
    {
        if (colorCoroutine != null) StopCoroutine(colorCoroutine);
        colorCoroutine = StartCoroutine(ColorRoutine(targetText));
    }

    private IEnumerator ColorRoutine(Color targetText)
    {
        Color startText = buttonText != null ? buttonText.color : normalTextColor;
        float elapsed = 0f;

        while (elapsed < colorDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / colorDuration);

            if (buttonText != null)
                buttonText.color = Color.Lerp(startText, targetText, t);

            yield return null;
        }

        if (buttonText != null) buttonText.color = targetText;
        colorCoroutine = null;
    }

    private void AnimateScale(Vector3 targetScale)
    {
        if (scaleCoroutine != null) StopCoroutine(scaleCoroutine);
        scaleCoroutine = StartCoroutine(ScaleRoutine(targetScale));
    }

    private IEnumerator ScaleRoutine(Vector3 targetScale)
    {
        Vector3 startScale = transform.localScale;
        float elapsed = 0f;

        while (elapsed < scaleDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / scaleDuration);
            t = t * t * (3f - 2f * t); // SmoothStep easing
            transform.localScale = Vector3.LerpUnclamped(startScale, targetScale, t);
            yield return null;
        }

        transform.localScale = targetScale;
        scaleCoroutine = null;
    }

    private void ApplyColors(bool hovered)
    {
        if (buttonText != null)
            buttonText.color = hovered ? hoverTextColor : normalTextColor;
    }

    private void PlaySound(AudioClip clip)
    {
        if (audioSource != null && clip != null)
            audioSource.PlayOneShot(clip, soundVolume);
    }

    private void OnDisable()
    {
        if (scaleCoroutine != null) StopCoroutine(scaleCoroutine);
        if (colorCoroutine != null) StopCoroutine(colorCoroutine);
        scaleCoroutine = null;
        colorCoroutine = null;
        isHovered = false;

        if (button != null) ApplyColors(false);
        transform.localScale = originalScale;
    }
}
