# Jellyfin IntroDB Skip Plugin (v2.3.0.0)

Este plugin para Jellyfin permite sincronizar marcadores de intro desde **IntroHater** e **IntroDB** sin necesidad de realizar un análisis pesado de los archivos multimedia locales. Es ideal para archivos `.strm` o bibliotecas en la nube.

## Características

- **Sin análisis local**: Utiliza únicamente metadatos (IMDb ID) para buscar los tiempos de intro y créditos en bases de datos comunitarias.
- **Doble Fuente**: Prioriza **IntroHater** (más fiable) y usa **IntroDB** como respaldo.
- **Salto Automático (Android)**: Fuerza el salto de intro desde el servidor para clientes Android que no muestran el botón nativo de Jellyfin.
- **Pre-sincronización**: Busca los marcadores del siguiente episodio automáticamente al empezar a ver un capítulo.
- **Totalmente en Español**: Interfaz y registros localizados.

## Configuración

1. Instala el plugin desde el repositorio.
2. Ve a la configuración del plugin en Jellyfin.
3. Asegúrate de que las URLs de IntroHater (`https://introhater.com`) e IntroDB (`https://api.introdb.app`) sean correctas.
4. (Opcional) Introduce tu API Key de IntroDB si tienes una.
5. Activa el "Android Exo fallback" si usas la App oficial de Android y quieres salto automático.

## Cómo funciona

El plugin monitoriza el inicio de las sesiones. Cuando detecta que un episodio de una serie no tiene marcadores, consulta las APIs externas. Si los encuentra, los inyecta en la base de datos de Jellyfin para que aparezca el botón de "Saltar Intro". En el caso de Android, el servidor envía una orden de salto forzado si se detecta que el reproductor está dentro del rango del intro.

---
*Desarrollado para la comunidad de Jellyfin.*
扫
