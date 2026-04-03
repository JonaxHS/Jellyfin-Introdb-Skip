# Jellyfin.Plugin.IntroDbSkip

Plugin de Jellyfin para sincronizar segmentos de intro desde [IntroDB](https://introdb.app/docs/api).

> Nota para bibliotecas `strm` (por ejemplo, Gelato): este plugin no analiza archivos locales ni usa ffmpeg.
> Todo el flujo depende de la API de IntroDB y de metadatos validos (`imdb_id`, `season`, `episode`).

## Que hace hoy

- Consulta `GET /segments` de IntroDB por `imdb_id + season + episode`.
- Resuelve segmentos en la marcha al reproducir un episodio (on-demand, sin escaneo global).
- Guarda localmente el marcador de intro (inicio/fin) para cada episodio encontrado.
- Publica el marcador como `Media Segment` de Jellyfin para habilitar el boton `Saltar intro`.
- Mantiene una tarea manual de backfill: `Sync IntroDB markers`.
- Guarda cache en el directorio de datos de Jellyfin: `introdbskip/markers.json`.

## Importante para STRM

- No se realiza analisis de video/audio sobre el archivo.
- No se requiere ffmpeg para detectar intros.
- Si faltan metadatos (`imdb_id`, temporada o episodio), no se puede consultar IntroDB para ese item.

## Estado

- Este MVP implementa sincronizacion on-demand + proveedor de media segments para Intro.
- El salto depende del cliente de Jellyfin y de que el episodio tenga metadata suficiente (`imdb/season/episode`).

## Configuracion

La configuracion del plugin (`PluginConfiguration`) incluye:

- `IntroDbBaseUrl`: por defecto `https://api.introdb.app`
- `SyncIntervalHours`: cada cuantas horas corre la tarea
- `MinimumConfidence`: confianza minima aceptada (0-1)
- `OverwriteExistingMarkers`: reservado para una fase de escritura sobre metadata local
- `SyncOnPlaybackStart`: sincroniza al iniciar reproduccion de episodios
- `Enabled`: activa/desactiva sincronizacion

## Build

Requiere .NET SDK y que la version de paquetes Jellyfin coincida con tu servidor.

```bash
dotnet restore
dotnet build
dotnet publish -c Release
```

## Instalacion manual en Jellyfin

1. Compilar el plugin.
2. Copiar el DLL generado a la carpeta de plugins de Jellyfin, por ejemplo:
   - Linux: `/var/lib/jellyfin/plugins/Jellyfin.Plugin.IntroDbSkip/`
   - macOS (docker o ruta equivalente): depende de tu volumen de datos
3. Reiniciar Jellyfin.
4. Ejecutar la tarea programada `Sync IntroDB markers` desde Dashboard.

## Instalacion por repositorio (Catalogo)

Tambien puedes instalarlo agregando un repositorio en Jellyfin, sin copiar DLL manualmente.

1. En Jellyfin ve a `Dashboard > Catalog > Repositories`.
2. Agrega este URL:

`https://raw.githubusercontent.com/JonaxHS/Jellyfin-Introdb-Skip/main/manifest.json`

3. Guarda, vuelve al Catalogo e instala `IntroDB Skip`.

## API del plugin

Endpoint admin para validar cache:

- `GET /IntroDbSkip/markers/{itemId}`

Devuelve el marcador sincronizado para un episodio especifico.
