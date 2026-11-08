# -*- coding: utf-8 -*-
"""イベント終了後に、参加者の写真や登録データを Firebase Realtime Database から削除する。

    # 何が削除されるかを確認する(何も削除しない)
    python tools/purge_event_data.py --auth <認証情報>

    # 写真だけを削除する(キャラクターの名前・能力値は残る)
    python tools/purge_event_data.py --auth <認証情報> --photos-only --yes

    # 登録データ・履歴・対戦データをすべて削除する
    python tools/purge_event_data.py --auth <認証情報> --all --yes

--auth には、Firebaseコンソール(プロジェクトの設定 → サービスアカウント → データベースのシークレット)の
シークレット、または /admins に登録したユーザーのIDトークンを指定する。
Database Rules で一般の書き込みが制限されているため、管理者の認証情報が必要になる。
"""
import argparse
import json
import sys
import urllib.parse
import urllib.request

DEFAULT_DATABASE_URL = "https://digitalcard-b825d-default-rtdb.firebaseio.com"


def request(method, url, auth, body=None):
    sep = "&" if "?" in url else "?"
    full = f"{url}{sep}auth={urllib.parse.quote(auth)}"
    data = None if body is None else json.dumps(body).encode("utf-8")
    req = urllib.request.Request(full, data=data, method=method, headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req) as res:
        text = res.read().decode("utf-8")
        return json.loads(text) if text else None


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--auth", required=True, help="データベースのシークレット、または管理者のIDトークン")
    parser.add_argument("--database-url", default=DEFAULT_DATABASE_URL)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--photos-only", action="store_true", help="写真(photoDataUrl)だけを削除する")
    mode.add_argument("--all", action="store_true", help="characterByCard・characters・tables をすべて削除する")
    parser.add_argument("--yes", action="store_true", help="確認のうえで実際に削除する(指定しなければ件数の表示のみ)")
    args = parser.parse_args()

    base = args.database_url.rstrip("/")
    cards = request("GET", f"{base}/characterByCard.json?shallow=true", args.auth) or {}
    history = request("GET", f"{base}/characters.json?shallow=true", args.auth) or {}
    tables = request("GET", f"{base}/tables.json?shallow=true", args.auth) or {}
    print(f"登録済みカード: {len(cards)} 件 / 登録履歴: {len(history)} 件 / 卓: {len(tables)} 件")

    if not args.photos_only and not args.all:
        print("削除するには --photos-only または --all と --yes を指定してください。")
        return 0
    if not args.yes:
        print("--yes が指定されていないため、削除は行いませんでした。")
        return 0

    if args.photos_only:
        if cards:
            request("PATCH", f"{base}/characterByCard.json", args.auth,
                    {f"{card_id}/photoDataUrl": None for card_id in cards})
        # 対戦枠に残っている写真も消す
        for table_id in tables:
            request("DELETE", f"{base}/tables/{table_id}/battleSlots.json", args.auth)
        print(f"写真を削除しました({len(cards)} 件)。")
    else:
        for path in ("characterByCard", "characters", "tables"):
            request("DELETE", f"{base}/{path}.json", args.auth)
        print("登録データ・履歴・対戦データをすべて削除しました。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
