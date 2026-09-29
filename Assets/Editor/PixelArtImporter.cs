// Assets/Resources/Art/ 의 도트 아트를 흐리지 않게 들여온다: 스프라이트, 점 필터, 압축·밉맵 없음.
// 폰트(.ttf)는 안티앨리어싱 없이 픽셀 그대로 그린다.
using UnityEditor;
using UnityEngine;

class PixelArtImporter : AssetPostprocessor
{
    const string ArtDir = "Assets/Resources/Art/";

    // 9-슬라이스 테두리 (px). 패널은 늘여도 모서리가 그대로다.
    static readonly Vector4 PanelBorder = new Vector4(12, 12, 12, 12);

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(ArtDir)) return;
        var ti = (TextureImporter)assetImporter;
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.filterMode = FilterMode.Point;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.mipmapEnabled = false;
        ti.alphaIsTransparency = true;
        ti.spriteBorder = assetPath.EndsWith("/panel.png") ? PanelBorder : Vector4.zero;
    }

    void OnPreprocessAsset()
    {
        if (!assetPath.StartsWith(ArtDir) || !assetPath.EndsWith(".ttf")) return;
        var fi = (TrueTypeFontImporter)assetImporter;
        fi.fontRenderingMode = FontRenderingMode.HintedRaster;
    }
}
