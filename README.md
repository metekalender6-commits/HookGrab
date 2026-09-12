# HookGrab

CS2 (CounterStrikeSharp) için basit hook & grab eklentisi.

- **`css_hook`** — yetki: `@css/ban` — bakış yönüne doğru fırlatır (grappling hook mantığı).
- **`css_grab`** — yetki: `@css/root` — baktığın oyuncuyu grablar; komutu tekrar çalıştırınca bırakır.
  - **Space** → grablanan oyuncuyu uzaklaştırır
  - **Ctrl** → grablanan oyuncuyu yaklaştırır
- **`css_hookspeed <sayı>`** — yetki: `@css/ban` — hook fırlatma hızını anlık değiştirir.

Ayrıca convar olarak da ayarlanabilir (server.cfg içine yazılabilir):

```
css_hook_speed 900
css_hook_upboost 100
css_grab_max_distance 2000
css_grab_min_dist 40
css_grab_max_dist 500
css_grab_move_speed 6
```

## Gereksinimler

- [Metamod:Source](https://www.sourcemm.net/downloads.php/?branch=master) (CS2 sürümü)
- [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp) sunucuya kurulu olmalı
- [.NET 8.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) (sadece derleme yapacağın makinede gerekli, sunucuda gerekmez)

## Kurulum (GitHub'dan)

### 1) Repoyu klonla

```bash
git clone https://github.com/<kullanici-adin>/HookGrab.git
cd HookGrab/HookGrab
```

### 2) Derle

```bash
dotnet restore
dotnet build -c Release
```

Derleme başarılı olursa çıktı şurada olacak:

```
HookGrab/HookGrab/bin/Release/net8.0/HookGrab.dll
```

> NuGet paketi bulunamazsa (`CounterStrikeSharp.API` restore hatası), CounterStrikeSharp'ın deploy paketindeki
> `addons/counterstrikesharp/managed/CounterStrikeSharp.API.dll` dosyasına doğrudan referans vermen gerekebilir.
> Bu durumda `.csproj` içindeki `<PackageReference>` satırını kaldırıp yerine:
> ```xml
> <ItemGroup>
>   <Reference Include="CounterStrikeSharp.API">
>     <HintPath>YOL/BURAYA/CounterStrikeSharp.API.dll</HintPath>
>     <Private>false</Private>
>   </Reference>
> </ItemGroup>
> ```
> satırlarını ekle.

### 3) Sunucuya kopyala

Derlenen `HookGrab.dll` dosyasını, CS2 sunucunda şu klasöre kopyala:

```
game/csgo/addons/counterstrikesharp/plugins/HookGrab/HookGrab.dll
```

(Klasör yoksa `HookGrab` adında bir klasör oluşturup içine at — CounterStrikeSharp her plugin'i kendi klasöründe arar.)

### 4) Sunucuyu yeniden başlat / plugin'i yükle

Sunucu konsolunda:

```
css_plugins load HookGrab
```

veya sunucuyu tamamen yeniden başlat.

### 5) Yetkileri ayarla

CounterStrikeSharp admin sisteminde (`addons/counterstrikesharp/configs/admins.json`) ilgili SteamID'lere
`@css/ban` (hook için) ve `@css/root` (grab için) yetkilerini ver.

## Notlar

- CounterStrikeSharp sürümü değiştikçe bazı API isimleri (`Buttons`, `EyeAngles`, `Teleport` vb.) değişebilir;
  derleme hatası alırsan hata mesajını paylaş, ona göre güncellerim.
- Grab sırasında hedefin kendi hareketi engellenmiyor, sadece pozisyonu her tick zorlanıyor.
