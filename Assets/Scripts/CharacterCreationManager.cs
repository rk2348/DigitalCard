using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro; // TextMeshProを使用。通常のUI.Textを使う場合は using UnityEngine.UI; に変更してください

/// <summary>
/// キャラクター作成シーンの制御。
/// スペースキーを押すとキャラクターが生成され、ステータスが自動で割り振られ、
/// GameManagerに保存される。同時に3Dオブジェクト（現在は仮のプリミティブ）を表示する。
/// その後バトルシーンへ遷移する。
///
/// 【セットアップ方法】
/// 1. キャラクター作成シーンに空のGameObjectを作成し、このスクリプトをアタッチ
/// 2. ステータス表示用のTextMeshProUGUIをCanvas上に配置し、statusText にドラッグ
/// 3. 3Dモデルを表示したい位置に空のGameObject（例："ModelSpawnPoint"）を配置し、
///    modelSpawnPoint にドラッグ（カメラが映せる位置に置いてください）
/// 4. characterModelPrefab は現時点では未設定でOK。
///    未設定の場合、属性ごとに色分けされた仮のカプセルが自動生成される。
///    後日、実際のキャラクターモデルのプレハブが用意できたら
///    ここにドラッグするだけで表示物が差し替わる。
/// 5. Build Settingsに "Battle" という名前のシーンを追加しておく
/// </summary>
public class CharacterCreationManager : MonoBehaviour
{
    [Tooltip("生成されるキャラクターの名前（必要であれば入力欄と連携させてください）")]
    [SerializeField] private string newCharacterName = "プレイヤー";

    [Tooltip("ステータス表示用のUIテキスト（任意）")]
    [SerializeField] private TextMeshProUGUI statusText;

    [Tooltip("バトルシーンへの遷移までの待機時間（秒）")]
    [SerializeField] private float delayBeforeSceneChange = 1.5f;

    [Tooltip("遷移先シーン名（Build Settingsに登録されているもの）")]
    [SerializeField] private string battleSceneName = "Battle";

    [Header("3Dモデル表示（後でキャラクターモデルに差し替え予定）")]
    [Tooltip("3Dオブジェクトを表示する位置・回転の基準にするTransform")]
    [SerializeField] private Transform modelSpawnPoint;

    [Tooltip("キャラクターモデルのプレハブ。未設定の場合は仮の3Dプリミティブ（カプセル）を表示する")]
    [SerializeField] private GameObject characterModelPrefab;

    private bool isCharacterCreated = false;
    private GameObject spawnedModelInstance;

    private void Update()
    {
        // まだキャラクターが作成されていない場合のみスペースキーを受け付ける
        if (!isCharacterCreated && Input.GetKeyDown(KeyCode.Space))
        {
            CreateCharacter();
        }
    }

    private void CreateCharacter()
    {
        isCharacterCreated = true;

        // 1. キャラクターを生成し、ステータスをランダムに割り振る
        CharacterStats newCharacter = new CharacterStats(newCharacterName);
        newCharacter.AssignRandomStats();

        // 2. GameManagerに保存する（シーンをまたいでも保持される）
        if (GameManager.Instance != null)
        {
            GameManager.Instance.SavePlayerCharacter(newCharacter);
        }
        else
        {
            Debug.LogError("GameManagerが見つかりません。タイトルシーンにGameManagerを配置してください。");
        }

        // 3. 画面にステータスを表示（UIがあれば）
        if (statusText != null)
        {
            string mutationHeader = newCharacter.isMutation ? "★突然変異が発生した！★\n" : "";
            statusText.text =
                $"{mutationHeader}{newCharacter.characterName} が誕生した！\n\n" +
                $"属性: {newCharacter.element}\n" +
                $"攻撃力: {newCharacter.attack}\n" +
                $"防御力: {newCharacter.defense}\n" +
                $"素早さ: {newCharacter.speed}\n\n" +
                $"スキル「{newCharacter.skill.skillName}」\n{newCharacter.skill.GetDescription()}";
        }

        // 4. 3Dオブジェクトを表示（現在は仮のプリミティブ、将来はキャラクターモデルに差し替え）
        SpawnCharacterModel(newCharacter);

        // 5. 少し待ってからバトルシーンへ遷移
        Invoke(nameof(GoToBattleScene), delayBeforeSceneChange);
    }

    /// <summary>
    /// キャラクターの3Dオブジェクトを生成して表示する。
    /// characterModelPrefab が設定されていればそれを使用（＝本番のキャラクターモデル差し替え口）。
    /// 未設定の場合は、属性に応じて色分けした仮のカプセルを表示する。
    /// </summary>
    private void SpawnCharacterModel(CharacterStats stats)
    {
        // 既に表示中のモデルがあれば削除（再生成に備えて）
        if (spawnedModelInstance != null)
        {
            Destroy(spawnedModelInstance);
        }

        Vector3 spawnPosition = modelSpawnPoint != null ? modelSpawnPoint.position : Vector3.zero;
        Quaternion spawnRotation = modelSpawnPoint != null ? modelSpawnPoint.rotation : Quaternion.identity;

        if (characterModelPrefab != null)
        {
            // 【将来ここが本番のキャラクターモデルに差し替わる想定】
            spawnedModelInstance = Instantiate(characterModelPrefab, spawnPosition, spawnRotation, modelSpawnPoint);
        }
        else
        {
            // プレハブ未設定時は仮の3Dプリミティブ（カプセル）を表示
            spawnedModelInstance = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            spawnedModelInstance.transform.SetPositionAndRotation(spawnPosition, spawnRotation);
            if (modelSpawnPoint != null)
            {
                spawnedModelInstance.transform.SetParent(modelSpawnPoint);
            }

            // 属性に応じて色を変える（仮の見た目分け）
            Renderer renderer = spawnedModelInstance.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = GetElementColor(stats.element);
            }
        }

        spawnedModelInstance.name = "CharacterModel_" + stats.characterName;

        // ゆっくり回転させてお披露目感を出す（任意の演出。不要であれば削除可）
        if (spawnedModelInstance.GetComponent<SimpleRotator>() == null)
        {
            spawnedModelInstance.AddComponent<SimpleRotator>();
        }
    }

    /// <summary>
    /// 属性ごとの仮の表示色。実際のカラーパレットが決まったら調整してください。
    /// </summary>
    private Color GetElementColor(ElementType element)
    {
        switch (element)
        {
            case ElementType.Fire: return new Color(0.9f, 0.3f, 0.2f);
            case ElementType.Wind: return new Color(0.4f, 0.8f, 0.4f);
            case ElementType.Thunder: return new Color(0.95f, 0.85f, 0.2f);
            case ElementType.Water: return new Color(0.2f, 0.5f, 0.9f);
            case ElementType.Earth: return new Color(0.6f, 0.4f, 0.2f);
            case ElementType.Light: return new Color(0.95f, 0.95f, 0.85f);
            default: return Color.white;
        }
    }

    private void GoToBattleScene()
    {
        SceneManager.LoadScene(battleSceneName);
    }
}