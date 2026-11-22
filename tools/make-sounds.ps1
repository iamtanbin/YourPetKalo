# Makes CatPet's meow sounds from sounds\source-meow.mp3.
# To use a different meow: replace that MP3, run this script, then run build.ps1.
$root = Split-Path $PSScriptRoot -Parent
Add-Type -Path "$PSScriptRoot\Mp3Decode.cs", "$PSScriptRoot\SoundTool.cs"
$rate = 0
$x = [Mp3Decode]::Decode("$root\sounds\source-meow.mp3", [ref]$rate)

#                         speed  softness(peak)
[SoundTool]::Make($x, $rate, 1.00, 0.17, "$root\sounds\meow.wav")  # the meow, soft and warm
[SoundTool]::Make($x, $rate, 0.85, 0.14, "$root\sounds\sad.wav")   # slower, lower, sleepy
[SoundTool]::Make($x, $rate, 1.15, 0.13, "$root\sounds\mrrp.wav")  # quicker, tiny and cute
Write-Host "Made sounds\meow.wav, sad.wav, mrrp.wav"
