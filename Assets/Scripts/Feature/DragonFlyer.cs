using System;
using System.Collections;
using UnityEngine;
using DG.Tweening;

/// <summary>
/// One dragon head flying a curved path, trailing a ribbon. Used by the Hold & Spin payout walk to
/// carry each held Orb's prize to the Winner panel. See Assets/Scripts/Feature/DragonFlyer.md.
///
/// This knows NOTHING about the game — not what an Orb is, what a prize is worth, or that a round is
/// ending. It is handed three points, a duration and two callbacks, and its whole job is to move
/// itself along that curve, face the right way, manage its own ribbon, and report when each stage is
/// done. The same component would serve any "thing flies from A to B" effect.
///
/// A world object, not UI: TrailRenderer is a 3D renderer and ignores Canvas sorting, so the head is
/// a SpriteRenderer too and the two sort against each other in one pipeline. It therefore also
/// ignores Mask / RectMask2D clipping and CanvasGroup alpha, all of which is fine here.
///
/// ONE instance is reused for every Orb rather than one spawned per Orb. That works only because the
/// walk waits for each ribbon to fade before starting the next dragon — repositioning mid-fade would
/// cut the ribbon off. See DragonFlyer.md section 8.
/// </summary>
public class DragonFlyer : MonoBehaviour
{
    [Header("Parts")]
    [Tooltip("The dragon head. Its art faces +X, which is what the rotation maths assumes.")]
    [SerializeField] private SpriteRenderer head;
    [Tooltip("The ribbon. Its own object, offset back along −X so the ribbon streams from behind the head rather than out of its face.")]
    [SerializeField] private TrailRenderer trail;

    [Header("Sorting")]
    [Tooltip("Nudges the dragon toward the camera so it draws in front of the canvas. MUST live here rather than on a parent: the flight writes world position every frame, and both endpoints sit exactly on the canvas plane, so a parent's Z would be overwritten on the first frame.")]
    [SerializeField] private float flyerZOffset = -1f;

    // The path's shape. Serialized rather than const — a deliberate exception to the project's
    // rule — because these are pure visual feel and want a slider, not a recompile, per change.
    // They live here rather than on the caller so every dial for the flight is on one component.
    [Header("Flight Path")]
    [Tooltip("How far along the line each control point sits, as a fraction of the path. Near 0 = tight hooks at both ends. Near 0.5 = one long lazy sweep.")]
    [SerializeField] private float controlSpread = 1.3f;

    [Tooltip("How far the controls push sideways, as a fraction of the path's length. This is the raw drama — 0.3 is a gentle arc, 1.0+ is wild.")]
    [SerializeField] private float bendAmount = 0.9f;

    [Tooltip("How much the bend differs between paths. 0 = every dragon flies the same shape. 1 = anything from flat to double the bend.")]
    [SerializeField] private float bendVariance = 0.6f;

    [Tooltip("How far past the target the second control reaches, as a fraction of the path. Makes the dragon overshoot and hook back in rather than arriving straight.")]
    [SerializeField] private float overshoot = 0.25f;

    [Tooltip("ON: the second control opposes the first, so the dragon weaves — an S-curve. OFF: both bend the same way, one big sweeping arc.")]
    [SerializeField] private bool sCurve = true;

    private Tween flightTween;
    private Coroutine ribbonRoutine;

    /// <summary>
    /// Flies from <paramref name="start"/> to <paramref name="end"/> along a curve this flyer
    /// derives for itself — the caller supplies only the two endpoints.
    ///
    /// Reports twice, and the two are deliberately separate:
    ///   onArrive — the head has landed. The caller's cue to bump its total.
    ///   onReady  — the ribbon has finished fading and this flyer can be repositioned.
    ///
    /// The caller waits for onReady rather than reading trail.time itself, so it never learns that a
    /// ribbon exists or how long one takes to clear. Swap the effect for a particle burst and the
    /// caller is untouched.
    /// </summary>
    internal void Fly(Vector3 start, Vector3 end, float duration, Action onArrive, Action onReady)
    {
        CancelFlight();

        BuildCurve(start, end, out Vector3 c1, out Vector3 c2);

        // Positioned BEFORE emitting is switched on. The other way round and the ribbon whips from
        // wherever this was last sitting to the first Orb.
        transform.position = WithOffset(start);
        FaceAlong(Tangent(start, c1, c2, end, 0f));

        if (trail != null)
        {
            trail.Clear();
            trail.emitting = true;
        }

        if (head != null) head.enabled = true;

        flightTween = DOVirtual.Float(0f, 1f, Mathf.Max(0.01f, duration), t =>
            {
                transform.position = WithOffset(Point(start, c1, c2, end, t));
                FaceAlong(Tangent(start, c1, c2, end, t));
            })
            .SetEase(Ease.Linear)
            .OnComplete(() =>
            {
                flightTween = null;

                // The landing is the beat the caller acts on. The ribbon fading afterwards is this
                // object's business and must not hold that up.
                onArrive?.Invoke();

                if (trail != null) trail.emitting = false;

                // Hide the SPRITE, not the GameObject. Deactivating would kill the ribbon instantly
                // instead of letting it fade.
                if (head != null) head.enabled = false;

                ribbonRoutine = StartCoroutine(WaitForRibbon(onReady));
            });
    }

    /// <summary>
    /// Stops a flight dead. Needed because the flight is a DOTween tween, not a coroutine — stopping
    /// the caller's sequence does NOT stop it, and it would carry on writing its position every
    /// frame, flying to a panel that has been switched off.
    /// </summary>
    internal void CancelFlight()
    {
        if (flightTween != null)
        {
            flightTween.Kill();
            flightTween = null;
        }

        if (ribbonRoutine != null)
        {
            StopCoroutine(ribbonRoutine);
            ribbonRoutine = null;
        }

        if (trail != null)
        {
            trail.emitting = false;
            trail.Clear();
        }

        if (head != null) head.enabled = false;
    }

    // A TrailRenderer's points expire trail.time seconds after being recorded, so once the head has
    // stopped and that long has passed the ribbon is genuinely empty — nothing left to streak from
    // when this gets repositioned for the next Orb.
    private IEnumerator WaitForRibbon(Action onReady)
    {
        float fade = trail != null ? trail.time : 0f;
        if (fade > 0f) yield return new WaitForSeconds(fade);

        ribbonRoutine = null;
        onReady?.Invoke();
    }

    private Vector3 WithOffset(Vector3 point)
    {
        return new Vector3(point.x, point.y, point.z + flyerZOffset);
    }

    // Rotates the head to point along its direction of travel. Z only — the art is 2D and facing
    // the camera, so a full 3D look-at would tip it out of plane.
    private void FaceAlong(Vector3 tangent)
    {
        if (tangent.sqrMagnitude <= Mathf.Epsilon) return;

        float angle = Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);
    }

    /// <summary>
    /// Derives the two control points that shape this flight.
    ///
    /// Cubic rather than quadratic because one control point can only ever bulge the path one way —
    /// turning the bend up makes a bigger arc, never a different shape. Two lets the path weave,
    /// hook, and overshoot.
    /// </summary>
    private void BuildCurve(Vector3 start, Vector3 end, out Vector3 c1, out Vector3 c2)
    {
        Vector3 dir = end - start;
        float length = dir.magnitude;

        if (length <= Mathf.Epsilon)
        {
            c1 = start;
            c2 = end;
            return;
        }

        Vector3 forward = dir / length;
        Vector3 perp = new Vector3(-forward.y, forward.x, 0f);

        // Which way this path bows. Taken from the start's position relative to the target, so
        // everything left of the panel arcs one way and everything right arcs the other — that
        // reads as intent, where a random side reads as a bug.
        float side = start.x < end.x ? 1f : -1f;

        // Per-path variation, hashed from the start position: no two Orbs fly the same shape, and
        // the same Orb always flies the same one. Deterministic, and needs nothing passed in — the
        // flyer still knows nothing about Orbs or indices.
        float hash = Mathf.Abs(Mathf.Sin((start.x * 12.9898f) + (start.y * 78.233f)) * 43758.5453f);
        float variant = hash - Mathf.Floor(hash);                       // 0..1
        float bend = length * bendAmount * (1f - bendVariance + (bendVariance * 2f * variant));

        c1 = start + (forward * (length * controlSpread)) + (perp * (bend * side));

        // The second control opposes the first for an S, or matches it for one big arc. Pushing it
        // past the end is what makes the dragon overshoot and hook back rather than arriving
        // straight on.
        float secondSide = sCurve ? -side : side;
        c2 = end - (forward * (length * controlSpread))
                 + (perp * (bend * secondSide))
                 + (forward * (length * overshoot));
    }

    // Cubic Bezier. Hand-rolled rather than DOTween's DOPath because the derivative below gives the
    // facing direction for free, where DOPath.SetLookAt orients in 3D.
    private static Vector3 Point(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float u = 1f - t;
        return (u * u * u * p0)
             + (3f * u * u * t * p1)
             + (3f * u * t * t * p2)
             + (t * t * t * p3);
    }

    private static Vector3 Tangent(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float u = 1f - t;
        return (3f * u * u * (p1 - p0))
             + (6f * u * t * (p2 - p1))
             + (3f * t * t * (p3 - p2));
    }

    private void OnDisable()
    {
        CancelFlight();
    }
}
