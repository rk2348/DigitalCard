using UnityEngine;

/// <summary>
/// スタジアムの中継カメラ。テレビ中継のようなアングル(引き・正面・肩越し・アップ・周回)を
/// 指定するだけで、現在の位置からなめらかに移動する(Cutを指定すると即座に切り替える)。
/// 被弾時の揺れ(Shake)もここで加える。
/// </summary>
[RequireComponent(typeof(Camera))]
public sealed class StadiumCamera : MonoBehaviour
{
    private Camera cam;
    private Vector3 targetPosition;
    private Vector3 targetLookAt;
    private float targetFov = 40f;
    private Vector3 currentLookAt;
    private Vector3 positionVelocity;
    private Vector3 lookVelocity;
    private float fovVelocity;
    private float smoothTime = 0.6f;

    // 周回モード
    private bool orbiting;
    private Vector3 orbitCenter;
    private float orbitRadius;
    private float orbitHeight;
    private float orbitSpeed;
    private float orbitAngle;

    // ゆっくり漂う動き(選択待ちのバトルビューで、画面が止まって見えないように)
    private float driftAmount;

    // 移動撮影(ドリー/クレーン): 開始位置から終了位置へ一定時間かけて動く
    private bool tracking;
    private Vector3 trackFrom, trackTo, trackLookFrom, trackLookTo;
    private float trackStart, trackDuration;

    // 揺れ
    private float shakeAmount;
    private float shakeSeed;

    public Camera Camera => cam != null ? cam : cam = GetComponent<Camera>();

    private void Awake()
    {
        cam = GetComponent<Camera>();
        targetPosition = transform.position;
        currentLookAt = targetLookAt = transform.position + transform.forward * 10f;
        targetFov = cam.fieldOfView;
        shakeSeed = Random.value * 100f;
    }

    /// <summary>指定位置から注視点を見るアングルへ移動する。</summary>
    public void Shot(Vector3 position, Vector3 lookAt, float fov, float blendSeconds = 0.6f, bool cut = false)
    {
        orbiting = false;
        tracking = false;
        driftAmount = 0f;
        targetPosition = position;
        targetLookAt = lookAt;
        targetFov = fov;
        smoothTime = Mathf.Max(0.01f, blendSeconds * 0.35f);
        if (cut) Snap();
    }

    /// <summary>注視点の周りを回り続ける(待機画面・登場・勝者の演出)。</summary>
    public void Orbit(Vector3 center, float radius, float height, float degreesPerSecond, float fov, float startAngle = float.NaN, bool cut = false)
    {
        orbiting = true;
        tracking = false;
        driftAmount = 0f;
        orbitCenter = center;
        orbitRadius = radius;
        orbitHeight = height;
        orbitSpeed = degreesPerSecond;
        if (!float.IsNaN(startAngle)) orbitAngle = startAngle;
        else
        {
            Vector3 offset = transform.position - center;
            orbitAngle = Mathf.Atan2(offset.z, offset.x) * Mathf.Rad2Deg;
        }
        targetFov = fov;
        smoothTime = 0.45f;
        UpdateOrbitTarget();
        if (cut) Snap();
    }

    // ==================== よく使うアングル ====================

    /// <summary>フィールド全体を横から見る、基本の中継アングル。</summary>
    public void Broadcast(float blend = 0.8f, bool cut = false)
    {
        Shot(new Vector3(0f, 5.2f, -21.5f), new Vector3(0f, 2.3f, 0f), 40f, blend, cut);
    }

    /// <summary>スタジアム全体の引きの画。</summary>
    public void Wide(float blend = 1.2f, bool cut = false)
    {
        Shot(new Vector3(0f, 16f, -38f), new Vector3(0f, 2f, 2f), 46f, blend, cut);
    }

    /// <summary>攻撃側の後ろ斜め上から、相手を見る肩越しのアングル。</summary>
    public void OverShoulder(Vector3 attacker, Vector3 defender, float blend = 0.5f, bool cut = false)
    {
        Vector3 toDefender = (defender - attacker).normalized;
        Vector3 position = attacker - toDefender * 6.5f + Vector3.up * 3.4f + Vector3.back * 4.2f;
        Shot(position, defender + Vector3.up * 1.8f, 38f, blend, cut);
    }

    /// <summary>
    /// ポケモンのバトル画面のような、斜め後ろからのアングル(選択を待っている間に使う)。
    /// 手前(near)のキャラクターを画面の左下に大きく、奥(far)のキャラクターを右寄りに小さく映し、
    /// ゆっくり漂わせる。手前には攻撃側を置き、ターンごとに入れ替わる。
    /// </summary>
    public void BattleView(Vector3 near, Vector3 far, float blend = 0.9f, bool cut = false)
    {
        Vector3 toFar = far - near;
        toFar.y = 0f;
        toFar.Normalize();
        // 観客席の正面側(-Z)へ回り込んだ位置から見る
        Vector3 side = Vector3.Cross(Vector3.up, toFar);
        if (side.z > 0f) side = -side;
        Vector3 position = near - toFar * BattleViewBack + side * BattleViewSide + Vector3.up * BattleViewHeight;
        Vector3 lookAt = Vector3.Lerp(near, far, BattleViewLookBias) + Vector3.up * 1.2f;
        Shot(position, lookAt, BattleViewFov, blend, cut);
        driftAmount = 1f;
    }

    /// <summary>
    /// 移動撮影: from から to へ、注視点も lookFrom から lookTo へ、duration 秒かけてゆっくり動かす。
    /// cut なら開始位置へ即座に切り替えてから動き出す。
    /// </summary>
    public void Track(Vector3 from, Vector3 to, Vector3 lookFrom, Vector3 lookTo, float fov, float duration, bool cut = false)
    {
        Shot(from, lookFrom, fov, cut ? 0.6f : 1.2f, cut);
        tracking = true;
        trackFrom = from;
        trackTo = to;
        trackLookFrom = lookFrom;
        trackLookTo = lookTo;
        trackStart = Time.time;
        trackDuration = Mathf.Max(0.1f, duration);
    }

    /// <summary>バトルビュー(BattleView)のカメラを、反対側(観客席の奥側)から見たもの。</summary>
    public void BattleViewReverse(Vector3 near, Vector3 far, float blend = 0.9f, bool cut = false)
    {
        Vector3 toFar = far - near;
        toFar.y = 0f;
        toFar.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, toFar);
        if (side.z < 0f) side = -side;
        Vector3 position = near - toFar * BattleViewBack + side * (BattleViewSide * 0.8f) + Vector3.up * (BattleViewHeight * 0.85f);
        Vector3 lookAt = Vector3.Lerp(near, far, BattleViewLookBias) + Vector3.up * 1.2f;
        Shot(position, lookAt, BattleViewFov, blend, cut);
        driftAmount = 1f;
    }

    // バトルビューの位置(手前のキャラクターからの距離)。画面の見え方はここで調整する
    private const float BattleViewBack = 6f;
    private const float BattleViewSide = 8f;
    private const float BattleViewHeight = 6f;
    private const float BattleViewLookBias = 0.45f;
    private const float BattleViewFov = 44f;

    /// <summary>キャラクターのアップ(正面やや斜め)。</summary>
    public void CloseUp(Vector3 subject, float height, float blend = 0.5f, bool cut = false, float side = 0f)
    {
        Vector3 position = subject + new Vector3(side * 3f, height * 0.55f + 0.6f, -7.5f);
        Shot(position, subject + Vector3.up * (height * 0.55f), 34f, blend, cut);
    }

    /// <summary>被弾などでカメラを揺らす。</summary>
    public void Shake(float amount)
    {
        shakeAmount = Mathf.Max(shakeAmount, amount);
    }

    private void UpdateOrbitTarget()
    {
        float rad = orbitAngle * Mathf.Deg2Rad;
        targetPosition = orbitCenter + new Vector3(Mathf.Cos(rad) * orbitRadius, orbitHeight, Mathf.Sin(rad) * orbitRadius);
        targetLookAt = orbitCenter;
    }

    private void Snap()
    {
        transform.position = targetPosition;
        currentLookAt = targetLookAt;
        Camera.fieldOfView = targetFov;
        positionVelocity = lookVelocity = Vector3.zero;
        fovVelocity = 0f;
        transform.LookAt(currentLookAt);
    }

    private void LateUpdate()
    {
        if (orbiting)
        {
            orbitAngle += orbitSpeed * Time.deltaTime;
            UpdateOrbitTarget();
        }
        else if (tracking)
        {
            float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Time.time - trackStart) / trackDuration));
            targetPosition = Vector3.Lerp(trackFrom, trackTo, p);
            targetLookAt = Vector3.Lerp(trackLookFrom, trackLookTo, p);
        }

        Vector3 drift = Vector3.zero;
        if (driftAmount > 0f)
        {
            float t = Time.time;
            drift = new Vector3(Mathf.Sin(t * 0.37f) * 0.8f, Mathf.Sin(t * 0.53f) * 0.3f, Mathf.Sin(t * 0.29f + 1.3f) * 0.6f) * driftAmount;
        }
        Vector3 position = Vector3.SmoothDamp(transform.position - lastShake, targetPosition + drift, ref positionVelocity, smoothTime);
        currentLookAt = Vector3.SmoothDamp(currentLookAt, targetLookAt, ref lookVelocity, smoothTime);
        Camera.fieldOfView = Mathf.SmoothDamp(Camera.fieldOfView, targetFov, ref fovVelocity, smoothTime);

        shakeAmount = Mathf.MoveTowards(shakeAmount, 0f, Time.deltaTime * 2.5f);
        Vector3 shake = ShakeOffset();
        transform.position = position + shake;
        transform.LookAt(currentLookAt + shake * 0.5f);
    }

    private Vector3 lastShake;

    private Vector3 ShakeOffset()
    {
        if (shakeAmount <= 0f) return lastShake = Vector3.zero;
        float t = Time.time * 28f;
        lastShake = new Vector3(Mathf.PerlinNoise(shakeSeed, t) - 0.5f, Mathf.PerlinNoise(shakeSeed + 10f, t) - 0.5f, 0f) * shakeAmount;
        return lastShake;
    }
}
