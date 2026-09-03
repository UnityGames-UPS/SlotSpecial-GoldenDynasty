using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using System.Collections;

public class OrientationChange : MonoBehaviour
{
    // Golden Dynasty is portrait-only, so nothing here rotates. The version this was taken from
    // forced landscape by turning the UI 90 degrees on tall screens; that rotation, and the
    // UIWrapper it acted on, are gone. All that adapts is the CanvasScaler's match value, which
    // letterboxes the 1080x1920 canvas — side margins on a wide desktop window, full-screen on a
    // phone.
    [Header("References")]
    [SerializeField] private CanvasScaler CanvasScaler;

    [Header("Transition")]
    [SerializeField] private float transitionDuration = 0.2f;
    [SerializeField] private float waitForRotation = 0.2f;
    private Vector2 referenceResolution;
    private Tween matchTween;
    private Coroutine rotationRoutine;

    // Last size the match was calculated for. Update polls against these — SwitchDisplay only
    // fires when the host page reports a resize, so without the poll nothing responds to the
    // window changing shape in the editor, or in a build whose host reports late or not at all.
    private int lastWidth = 0;
    private int lastHeight = 0;

    private void Awake()
    {
        referenceResolution = CanvasScaler.referenceResolution; 

        ApplyMatch(Screen.width, Screen.height, instant: true);
    }

    void SwitchDisplay(string dimensions)
    {
        if (rotationRoutine != null) StopCoroutine(rotationRoutine);
        rotationRoutine = StartCoroutine(RotationCoroutine(dimensions));
    }

    IEnumerator RotationCoroutine(string dimensions)
    {
        yield return new WaitForSecondsRealtime(waitForRotation);

        string[] parts = dimensions.Split(',');
        if (parts.Length == 2
            && int.TryParse(parts[0], out int w)
            && int.TryParse(parts[1], out int h)
            && w > 0 && h > 0)
        {
            ApplyMatch(w, h, instant: false);
        }
        else
        {
            Debug.LogWarning("[OrientationChange] Invalid dimensions: " + dimensions);
        }
    }

    private void ApplyMatch(int screenW, int screenH, bool instant)
    {
        lastWidth = screenW;
        lastHeight = screenH;

        float refW = referenceResolution.x;
        float refH = referenceResolution.y;

        float widthScale = screenW / refW;
        float heightScale = screenH / refH;

        float targetMatch;
        if (Mathf.Abs(heightScale - widthScale) < 0.0001f)
        {
            targetMatch = 0.5f;
        }
        else
        {
            // Fit the whole canvas inside the window. The version this came from picked between
            // this and an axis-swapped variant, because its other branch was the rotated one —
            // screen height mapped to canvas width there. Nothing rotates now, so the swap is
            // always wrong and only this remains.
            float targetScale = Mathf.Min(widthScale, heightScale);
            float logRatio = Mathf.Log(heightScale / widthScale);
            targetMatch = Mathf.Clamp01(Mathf.Log(targetScale / widthScale) / logRatio);
        }

        if (instant)
        {
            CanvasScaler.matchWidthOrHeight = targetMatch;
            return;
        }

        if (matchTween != null && matchTween.IsActive()) matchTween.Kill();
        matchTween = DOTween
          .To(
            () => CanvasScaler.matchWidthOrHeight,
            x => CanvasScaler.matchWidthOrHeight = x,
            targetMatch,
            transitionDuration)
          .SetEase(Ease.InOutQuad);
    }
    private void Update()
    {
        if (Screen.width != lastWidth || Screen.height != lastHeight)
        {
            ApplyMatch(Screen.width, Screen.height, instant: false);
        }

#if UNITY_EDITOR
        if (Input.GetKeyDown(KeyCode.Space))
            SwitchDisplay($"{Screen.width},{Screen.height}");
#endif
    }
}