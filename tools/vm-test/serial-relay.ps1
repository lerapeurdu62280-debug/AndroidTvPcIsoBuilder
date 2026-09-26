# Relais port série VirtualBox (tcpserver 2323) : sortie -> serial_tcp.log, commandes <- cmd.txt
# (chaque nouvelle ligne ajoutée à cmd.txt est envoyée à la console Android).
# Prérequis : VBoxManage modifyvm ATV-Test --uart1 0x3F8 4 --uart-mode1 tcpserver 2323, et une ISO
# de diagnostic dont les arguments noyau finissent par console=ttyS0,115200 (voir SilentBootConfig).
param([string]$OutDir = (Join-Path $env:TEMP 'atv-serial'))
$d = $OutDir
New-Item -ItemType Directory -Force $d | Out-Null
$log = Join-Path $d 'serial_tcp.log'
$cmd = Join-Path $d 'cmd.txt'
Remove-Item $log, $cmd -ErrorAction SilentlyContinue
$client = $null
while (-not $client) {
    try { $client = [System.Net.Sockets.TcpClient]::new('127.0.0.1', 2323) } catch { Start-Sleep -Milliseconds 300 }
}
$s = $client.GetStream()
$buf = New-Object byte[] 65536
$sent = 0
$fs = [System.IO.File]::Open($log, 'Append', 'Write', 'ReadWrite')
while ($client.Connected) {
    while ($s.DataAvailable) {
        $n = $s.Read($buf, 0, $buf.Length)
        $fs.Write($buf, 0, $n); $fs.Flush()
    }
    if (Test-Path $cmd) {
        $lines = @(Get-Content $cmd)
        for ($i = $sent; $i -lt $lines.Count; $i++) {
            $b = [System.Text.Encoding]::ASCII.GetBytes($lines[$i] + "`n")
            $s.Write($b, 0, $b.Length)
        }
        $sent = $lines.Count
    }
    Start-Sleep -Milliseconds 200
}
