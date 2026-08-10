using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Orisamo.Battle;

namespace Orisamo.Network
{
    /// <summary>
    /// 対戦結果（battleLog / result）をサーバーへ送信する。
    /// 大会運営・レアリティ統計等への活用を想定（POST {apiBaseUrl}/battle-results）。
    /// </summary>
    public class ResultReporter : MonoBehaviour
    {
        [SerializeField] private string apiBaseUrl = "https://example.com/api";
        [SerializeField] private int timeoutSeconds = 8;

        public event Action OnReportSucceeded;
        public event Action<string> OnReportFailed;

        public void Report(BattleSession session)
        {
            if (session == null)
            {
                Debug.LogError("ResultReporter: sessionがnullです。");
                return;
            }
            StartCoroutine(SendReport(session));
        }

        private IEnumerator SendReport(BattleSession session)
        {
            string json = JsonUtility.ToJson(BattleReportDto.From(session));
            byte[] body = Encoding.UTF8.GetBytes(json);

            using var req = new UnityWebRequest($"{apiBaseUrl}/battle-results", "POST");
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = timeoutSeconds;

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                string err = $"結果送信に失敗しました: {req.error}";
                Debug.LogWarning(err);
                OnReportFailed?.Invoke(err);
                yield break;
            }

            OnReportSucceeded?.Invoke();
        }
    }

    /// <summary>サーバー送信用のDTO。JsonUtilityでシリアライズする。</summary>
    [Serializable]
    internal class BattleReportDto
    {
        public string sessionId;
        public string[] participantIds;
        public string result;
        public string[] battleLog;

        public static BattleReportDto From(BattleSession session)
        {
            return new BattleReportDto
            {
                sessionId = session.SessionId,
                participantIds = session.GetParticipantIds(),
                result = session.Result.ToString(),
                battleLog = session.BattleLog.ToArray()
            };
        }
    }
}
