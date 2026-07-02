sed -i 's/using var dialog = new System.Windows.Forms.FolderBrowserDialog/return StatusCode(501, "Not supported"); \/\/ using var dialog = new System.Windows.Forms.FolderBrowserDialog/g' src/WISE.Api/Controllers/SystemController.cs
sed -i 's/Description = "フォルダを選択してください",/\/\/ Description = "フォルダを選択してください",/g' src/WISE.Api/Controllers/SystemController.cs
sed -i 's/UseDescriptionForTitle = true,/\/\/ UseDescriptionForTitle = true,/g' src/WISE.Api/Controllers/SystemController.cs
sed -i 's/ShowNewFolderButton = true,/\/\/ ShowNewFolderButton = true,/g' src/WISE.Api/Controllers/SystemController.cs
sed -i 's/};/\/\/ };/g' src/WISE.Api/Controllers/SystemController.cs
sed -i 's/if (!string.IsNullOrWhiteSpace(initialPath) && System.IO.Directory.Exists(initialPath))/\/\/ if (!string.IsNullOrWhiteSpace(initialPath) \&\& System.IO.Directory.Exists(initialPath))/g' src/WISE.Api/Controllers/SystemController.cs
sed -i 's/dialog.InitialDirectory = initialPath;/\/\/ dialog.InitialDirectory = initialPath;/g' src/WISE.Api/Controllers/SystemController.cs
sed -i 's/var result = dialog.ShowDialog();/\/\/ var result = dialog.ShowDialog();/g' src/WISE.Api/Controllers/SystemController.cs
sed -i 's/if (result == System.Windows.Forms.DialogResult.OK)/\/\/ if (result == System.Windows.Forms.DialogResult.OK)/g' src/WISE.Api/Controllers/SystemController.cs
sed -i 's/selectedPath = dialog.SelectedPath;/\/\/ selectedPath = dialog.SelectedPath;/g' src/WISE.Api/Controllers/SystemController.cs
