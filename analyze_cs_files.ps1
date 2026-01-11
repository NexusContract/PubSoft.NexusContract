# NexusContract C# 文件统计脚本
# 输出格式：文件名 | 代码行数 | 方法签名列表

param(
    [string]$RootPath = "d:\geek\NexusContract\src"
)

Write-Host "=== NexusContract C# 文件统计 ===" -ForegroundColor Cyan
Write-Host "分析目录: $RootPath" -ForegroundColor Yellow
Write-Host ""

# 获取所有 .cs 文件
$csFiles = Get-ChildItem -Path $RootPath -Recurse -Filter "*.cs" | Where-Object { -not $_.FullName.Contains("\bin\") -and -not $_.FullName.Contains("\obj\") }

$totalFiles = $csFiles.Count
Write-Host "找到 $totalFiles 个 C# 文件" -ForegroundColor Green
Write-Host ""

foreach ($file in $csFiles) {
    $relativePath = $file.FullName.Replace("$RootPath\", "").Replace("$RootPath", "")
    Write-Host "📄 $relativePath" -ForegroundColor White

    try {
        $content = Get-Content -Path $file.FullName -Raw

        # 统计代码行数（排除空行和注释）
        $lines = Get-Content -Path $file.FullName
        $codeLines = 0
        foreach ($line in $lines) {
            $trimmed = $line.Trim()
            if ($trimmed -and -not $trimmed.StartsWith("//") -and -not $trimmed.StartsWith("/*") -and -not $trimmed.StartsWith("*")) {
                $codeLines++
            }
        }

        Write-Host "   代码行数: $codeLines" -ForegroundColor Gray

        # 提取方法签名
        $methodSignatures = @()

        # 匹配方法定义的正则表达式
        $methodPattern = '(?:public|private|protected|internal)\s+(?:static\s+)?(?:async\s+)?(?:\w+\s+)+\w+\s*\([^)]*\)'
        $matches = [regex]::Matches($content, $methodPattern)

        foreach ($match in $matches) {
            $signature = $match.Value.Trim()
            # 清理签名格式
            $signature = $signature -replace '\s+', ' '
            $methodSignatures += $signature
        }

        if ($methodSignatures.Count -gt 0) {
            Write-Host "   方法签名:" -ForegroundColor Gray
            foreach ($sig in $methodSignatures) {
                Write-Host "     • $sig" -ForegroundColor DarkGray
            }
        } else {
            Write-Host "   方法签名: 无" -ForegroundColor DarkGray
        }

    } catch {
        Write-Host "   ❌ 读取失败: $($_.Exception.Message)" -ForegroundColor Red
    }

    Write-Host ""
}

Write-Host "=== 统计完成 ===" -ForegroundColor Cyan