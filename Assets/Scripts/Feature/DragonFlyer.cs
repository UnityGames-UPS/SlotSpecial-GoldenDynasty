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
    // rule — because these are pure visual feel and want tuning in the Inspector, not a recompile,
    // per change. They live here rather than on the caller so every dial for the flight is on one
    // component. No [Range] caps: a value that turns out to want 2.5 should not need a code edit to
    // get there.
    //
    // Both offsets are in WORLD axes, not axes relative to the direction of travel. That is the
    // whole point of them. See BuildCurve.
    [Header("Flight Path")]
    [Tooltip("Where the dragon heads FIRST, as a fraction of the path's length. X is INWARD — toward the middle of the flight bounds — so it flips sign either side of the board. Y is world vertical: negative dives into the reel area.")]
    [SerializeField] private Vector2 startOffset = new Vector2(1.8f, -0.3f);

    [Tooltip("How the dragon comes IN to the panel. Same units and axes as the start offset. Its X must OPPOSE the start offset's for the path to cross itself into a loop — matching signs give one smooth sweep instead.")]
    [SerializeField] private Vector2 endOffset = new Vector2(-1.2f, -1.2f);

    [Tooltip("How much the shape differs between Orbs, hashed from position so a given Orb always flies the same path. WARNING: loops are sensitive to this — much above 0.15 and the scaled-down paths stop crossing themselves. Variety and loops trade against each other.")]
    [SerializeField] private float bendVariance = 0.1f;

    [Tooltip("Ceiling for both control points, as a fraction of the flight bounds' height up from its bottom. This is what keeps the loop DOWN in the reel area rather than arcing up over the board.")]
    [SerializeField] private float controlCeiling = 0.9f;

    private Tween flightTween;
    private Coroutine ribbonRoutine;

    // The region the path should stay inside and bow toward. Optional: without it the flight still
    // runs, it just cannot orient itself relative to the board.
    private Rect flightBounds;
    private bool hasFlightBounds;

    /// <summary>
    /// Hands this flyer the region its path should stay inside and bow toward, in WORLD space.
    ///
    /// Deliberately a plain Rect rather than anything that names the board. The caller owns game
    /// geometry, this component owns motion, and "stay inside this, bow toward its middle" is the
    /// whole of what has to cross between them — which is what lets this stay a component that
    /// would serve any "thing flies from A to B" effect.
    ///
    /// Set once before a walk rather than passed on every Fly, since it is constant for the round.
    /// Without it the flight still works: "inward" falls back to the target and the ceiling is
    /// skipped, which is the old behaviour.
    /// </summary>
    internal void SetFlightBounds(Rect worldBounds)
    {
        flightBounds = worldBounds;
        hasFlightBounds = true;
    }

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
    /// hook, and cross itself into a loop.
    ///
    /// The offsets are in WORLD axes rather than axes relative to the direction of travel, and that
    /// is the whole point of them. An Orb-to-panel path points roughly upward, so its perpendicular
    /// is roughly horizontal AND rotates as the path does — in path-relative terms neither "down"
    /// nor "toward the middle of the board" can be expressed at all. This is why the previous
    /// version could only ever bow sideways no matter how its numbers were tuned.
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

        // Per-path variation, hashed from the start position: no two Orbs fly quite the same shape,
        // and the same Orb always flies the same one. Deterministic, and needs nothing passed in —
        // the flyer still knows nothing about Orbs or indices.
        float hash = Mathf.Abs(Mathf.Sin((start.x * 12.9898f) + (start.y * 78.233f)) * 43758.5453f);
        float variant = hash - Mathf.Floor(hash);                       // 0..1
        float scale = 1f - bendVariance + (bendVariance * 2f * variant);

        // Which way the middle of the board lies from this Orb, so the two columns either side of
        // centre bow toward it rather than out past the edge.
        //
        // Measured against the BOUNDS, not against the target. The panel is a single fixed point,
        // so comparing against it says which way the path bows relative to the panel — it cannot
        // know where the middle column is. With no bounds set the target is the only reference
        // available, which reproduces the old behaviour rather than flattening the path.
        float centreX = hasFlightBounds ? flightBounds.center.x : end.x;
        float inward = start.x <= centreX ? 1f : -1f;

        c1 = new Vector3(start.x + (length * startOffset.x * scale * inward),
                         start.y + (length * startOffset.y * scale),
                         start.z);

        c2 = new Vector3(end.x + (length * endOffset.x * scale * inward),
                         end.y + (length * endOffset.y * scale),
                         end.z);

        // The ceiling is what keeps the loop DOWN in the reel area. A Bezier never leaves the convex
        // hull of its control points, so with both controls under this line the only thing that can
        // carry the path above it is an endpoint — which is exactly the final climb into the panel.
        // Containment by construction, rather than by tuning until it looks contained.
        if (hasFlightBounds)
        {
            float ceiling = flightBounds.yMin + (flightBounds.height * controlCeiling);
            if (c1.y > ceiling) c1.y = ceiling;
            if (c2.y > ceiling) c2.y = ceiling;
        }
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