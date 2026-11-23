# 共通「情報」タブ

依存定義が0件のプラグインも共通ソースを組み込むが、DrawStandardTrailingTabs の drawInfo に null を渡し、タブ見出し自体を表示しない。判定はインストール済み件数ではなく定義件数で行う。必須が0件でも機能限定の依存が1件以上あれば表示する。
正本は同じフォルダーの StandardPluginInfo.cs と StandardPluginTabs.cs。各リポジトリに両ファイルを同一内容で取り込み、StandardPluginInfo をウィンドウごとに一度生成する。PluginDependency に InternalName、表示名、必須区分、7言語の用途、任意の注記・公式HTTPS導入ガイドを指定する。DrawStandardTrailingTabs の末尾引数に StandardPluginInfo.Title(code) と () => info.Draw(pluginInterface, code) を渡す。順序は設定 → 情報 → 言語設定 → 支援。既存呼出しは追加引数なしでも動作する。
初回表示と再確認ボタンだけでローカル状態を読む。導入は本人のクリックで検索済みDalamudインストーラーを開き、追加リポジトリが必要なら導入案内を利用する。自動インストールやURL自動登録はしない。
