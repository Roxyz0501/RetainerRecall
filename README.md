# Retainer Recall

Author: **Roxyz0501** · Dalamud API 15 · .NET 10 · Windows x64

リテイナーの販売リスト左下に、出品中のアイテムを一括で回収する2つのボタンを追加します。

- **すべて所持品に戻す**: プレイヤーの所持品へ回収。
- **すべてリテイナーに戻す**: 現在のリテイナーの所持品へ回収。
- `/retainerrecall`: 各ボタンの表示オン・オフ、待機秒数（0.5～30秒、初期値1.5秒）、販売リスト左下からのX/Y位置を設定。
- `/retainerrecall stop` または実行中の「停止」ボタンで停止。

## インストール

Dalamudのカスタムプラグインリポジトリへ以下を追加し、**Retainer Recall** をインストールしてください。

`https://raw.githubusercontent.com/Roxyz0501/DalamudPluginRepo/main/repo.json`

## 動作と制限

初期公開版です。Releaseビルドと独立した回収状態機械のテストを実施していますが、ゲーム内のネイティブ操作・配置・実際の回収は未検証です。

1. リテイナーの販売リストを開き、移動先のボタンを押します。
2. 設定秒数後、実在する出品スロット1件をゲームの標準回収関数へ渡します。
3. 出品からの削除と移動先の数量増加を両方確認し、設定秒数を待って次へ進みます。

移動先に空き枠を1つ以上確保してください。スタックに空きがあっても空き枠が0なら停止する保守的な仕様です。所持品データが未読込の場合も停止します。その場合はリテイナーの所持品を一度開いてから販売リストに戻ってください。通常の所持品ページとクリスタル収納の数量を回収確認の対象とします。

実行中は手動操作や他の自動化を併用しないでください。販売リストを閉じる、リテイナーやキャラクターを変更する、別メニューを開く、出品内容が変わる、15秒以内に回収結果を確認できない場合は停止します。自動再試行は行いません。停止前にゲームへ渡した1件は完了する場合があります。

独自パケットの生成・送信、ネットワークフック、所持品データの書き換えは行いません。FFXIVClientStructsに公開された `InventoryManager.MoveFromRetainerMarketToPlayerInventory` / `MoveFromRetainerMarketToRetainerInventory` を使用します。通信自体はゲームの通常処理に委ねます。ゲーム更新による互換性やサーバー側受理を保証するものではありません。

## 開発・参照元

このプロジェクトはユーザー指定により**新規独立プラグイン**として開始しました。既存プラグインのリポジトリをコピーして作成したものではありません。

- [Dalamud](https://github.com/goatcorp/Dalamud): goatcorp and contributors、AGPL-3.0。ホストAPI、設定保存、UI、ライフサイクルを使用。ホストDLLは同梱しません。
- [FFXIVClientStructs](https://github.com/aers/FFXIVClientStructs): aers and contributors、MIT。出品・所持品・リテイナーの構造体定義と既存の回収関数を参照・使用。ライブラリはDalamudが提供し、同梱しません。
- [Marketbuddy](https://github.com/PunishXIV/Marketbuddy): Chalkos、NightmareXIV and contributors、Apache-2.0。販売リスト追従オーバーレイの挙動を調査。コードのコピーはしていません。詳細はTHIRD-PARTY-NOTICES.md。

`dotnet build -c Release` / `dotnet run --project Tests -c Release`

アイコンは本プロジェクト向けのオリジナル生成画像です。第三者・ゲームのアートワークは使用していません。

## 支援

任意の開発支援: [Roxyz0501 on Ko-fi](https://ko-fi.com/roxyz0501)。支援の有無による機能制限はありません。
