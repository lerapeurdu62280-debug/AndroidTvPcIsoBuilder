# Série de démarrages automatiques de la VM VirtualBox "ATV-Test" sur l'ISO déjà attachée.
# Pour chaque démarrage : captures à 45 s (logo attendu), 115 s et 130 s. Si les deux
# dernières sont identiques, l'écran est figé (l'assistant Android TV s'anime en continu).
#   pwsh -File boot-series.ps1 -Tag fix -Count 5
# Réglages VM requis : --cpu-profile "AMD Ryzen 7 1800X Eight-Core" (sinon le self-test
# BoringSSL 32 bits plante et Android redémarre), Hyper-V désactivé sur l'hôte.
param(
    [string]$Tag = 'serie',
    [int]$Count = 5,
    [string]$OutDir = (Join-Path $env:TEMP 'atv-boot-series')
)
$vbm = 'C:\Program Files\Oracle\VirtualBox\VBoxManage.exe'
$vboxLog = 'D:\Logiciels_SOSINFOLUDO\VMs\ATV-Test\Logs\VBox.log'
New-Item -ItemType Directory -Force $OutDir | Out-Null
function Shot($name) {
    $p = Join-Path $OutDir "$name.png"
    & $vbm controlvm ATV-Test screenshotpng $p 2>&1 | Out-Null
    if (Test-Path $p) { (Get-FileHash $p -Algorithm MD5).Hash.Substring(0, 8) } else { 'ECHEC' }
}
for ($i = 1; $i -le $Count; $i++) {
    & $vbm controlvm ATV-Test poweroff 2>&1 | Out-Null
    # Ne pas sonder showvminfo pendant l'arrêt : il se bat pour le verrou de session.
    Start-Sleep 8
    for ($k = 0; $k -lt 10; $k++) {
        $out = & $vbm startvm ATV-Test --type headless 2>&1
        if ($LASTEXITCODE -eq 0) { break }
        Start-Sleep 3
    }
    if ($LASTEXITCODE -ne 0) { Write-Output "$Tag run $i : startvm ECHEC $out"; continue }
    $t0 = Get-Date
    $h45 = $null
    foreach ($t in 45, 115, 130) {
        while (((Get-Date) - $t0).TotalSeconds -lt $t) { Start-Sleep -Milliseconds 500 }
        Set-Variable "h$t" (Shot "${Tag}_${i}_$t")
    }
    $etat = if ($h115 -eq $h130) { 'FIGE' } else { 'VIVANT' }
    $resets = (Select-String $vboxLog -Pattern 'Reset initiated').Count
    Write-Output "$Tag run $i : $etat (45s=$h45 115s=$h115 130s=$h130 resets=$resets)"
}
& $vbm controlvm ATV-Test poweroff 2>&1 | Out-Null
Write-Output "Captures : $OutDir"
