# Оригинальные текстуры окружения

Созданы 4 октября 2026 года встроенным `image_gen` по навыку `imagegen`. API/CLI не использовались. Исходные изображения осмотрены; у листвы подтверждён настоящий канал прозрачности RGBA (0–255). Все выбранные файлы скопированы в проект, внешняя папка генерации для запуска игры не требуется.

| Файл в проекте | Назначение |
|---|---|
| `UnityProject/Assets/ZoneZ/Resources/Textures/weathered-concrete.png` | Бетон и выцветшая штукатурка |
| `UnityProject/Assets/ZoneZ/Resources/Textures/worn-asphalt.png` | Дорожки |
| `UnityProject/Assets/ZoneZ/Resources/Textures/weathered-timber.png` | Деревянные поверхности |
| `UnityProject/Assets/ZoneZ/Resources/Textures/dry-park-ground.png` | Земля, газон и дальний рельеф |
| `UnityProject/Assets/ZoneZ/Resources/Textures/oak-foliage.png` | Прозрачные плоскости листвы в объёмных кронах |

Все пути указаны относительно корня проекта.

Unity импортирует текстуры с mipmaps, максимальным размером 1024, анизотропией 8 и сжатием. Листва использует отсечение по альфа-каналу; остальные изображения повторяются. Текстуры не определяют коллизии и прострелы: их правила сохраняются в SurfaceTag.

10 октября добавлена объёмная трава: `GrassArt.cs` создаёт невысокие изогнутые травинки из непрозрачной геометрии в трёх оттенках. Пучки объединяются в пространственные блоки по 12 м; размещение учитывает коллайдеры дорожек, зданий и предметов. Сама трава не имеет коллайдеров. Новые изображения для неё не требуются.

Земля и газон разделены по высоте на 7 см. Видимые дорожки собираются в один меш без перекрытий на пересечениях, с непрерывными UV и прежними коллайдерами. Это устраняет конкурирующие поверхности, вызывавшие мерцание; фильтрация текстур сохраняет mipmaps и анизотропию 8.

## Запросы генерации

### weathered-concrete

Use case: photorealistic-natural. Asset type: square seamlessly tileable albedo texture for a 3D first-person game, abandoned zoo exterior concrete. A flat orthographic close-up scan of aged warm gray poured concrete and worn light limestone plaster, subtle irregular discoloration, fine aggregate, very small hairline cracks and faint mottled weathering. Physically plausible moderately realistic tactical shooter material, not stylized noise. Uniform diffuse illumination, no baked shadows, no directional lighting, no objects, no perspective, no text or logos, no border. Even detail density, low to medium contrast, bright midtones so the material can be tinted in Unity. Edges must tile seamlessly horizontally and vertically. Fill the complete square with only the material surface.

### worn-asphalt

Use case: photorealistic-natural. Asset type: square seamlessly tileable albedo texture for an abandoned zoo walkway in a 3D first-person game. Flat orthographic overhead surface scan of sun-bleached gray asphalt, small embedded aggregate and thin irregular shallow cracks, sparse traces of dry dust and faded gritty concrete repairs. Moderately realistic tactical shooter environment material. Uniform diffuse illumination with no baked shadows, no lighting gradients, no perspective, no raised objects, no road markings, no text, no leaves, no border. Medium-light neutral gray palette, subtle variation across the entire surface; avoid strong distinctive patches that reveal repetition. The square must tile seamlessly on all edges.

### weathered-timber

Use case: photorealistic-natural. Asset type: square tileable albedo texture for 3D wooden doors, benches and boxes in an abandoned zoo. Flat orthographic close-up of lightly weathered sun-bleached oak timber, fine longitudinal wood grain running vertically, subtle variation in muted warm beige and light brown, a few tiny worn scratches and subtle knots, no plank seams. Moderately realistic game texture under completely even diffuse lighting. No shadows, no perspective, no text, no frame, no objects, no paint. No strong dark knots or dominant distinctive shapes. Seamless repeat at all four edges. Fill the entire image only with the flat wood surface.

### dry-park-ground

Use case: photorealistic-natural. Asset type: square seamlessly tileable ground albedo for a realistic abandoned park/zoo in a 3D game. Strictly orthographic overhead view of short patchy dry olive green grass mixed with small irregular areas of dusty light brown earth. Fine small blades and natural subtle uneven ground texture, restrained tiny dry straw scattered among grass, overall consistent medium value and low contrast. Uniform ambient lighting without any directionality or baked shadows. No rocks larger than tiny gravel, no objects, no flowers, no large tall tufts, no lines, no paths, no text or border. Edges must repeat seamlessly horizontally and vertically. This is a flat material texture only, no perspective and no scene.

### oak-foliage

Use case: photorealistic-natural. Asset type: transparent cutout albedo for foliage cards in a realistic 3D tactical shooter. A small loose branching spray of about thirty small European oak leaves, natural slightly dusty olive green with muted yellow-green variation, a few thin brown twigs. Orthographic front view, all leaves reasonably close to the same plane, soft even ambient illumination, no directional shadows or rim light. Delicate varied leaf silhouettes, some leaves face camera and a few are angled; asymmetrical natural branching shape with irregular gaps between leaves. This is one compact foliage cluster for repeating on 3D tree canopy geometry, not an entire tree and not a scene. Completely transparent background, including gaps between leaves. No pot, ground, sky, text, border, watermark, floating particles or cast shadows. Fine crisp alpha edges without white halo. Leave transparent padding around the cluster so no leaf touches an edge.

Для `oak-foliage` использован `transparent_background=true`, для остальных — `false`. Изображения служат собственными материалами проекта; они не извлечены из CS2 или Standoff 2.

