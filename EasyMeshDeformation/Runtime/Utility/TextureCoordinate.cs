namespace EasyMeshDeformation
{
	/// <summary>
	/// 可用的纹理坐标（UV）集合，对应Mesh中的 TexCoord0 ~ TexCoord7 共 8 组 UV，用于遮罩系统选择采样依据。
	/// </summary>
	public enum TextureCoordinate : int
	{
		TexCoord0 = 0,
		TexCoord1 = 1,
		TexCoord2 = 2,
		TexCoord3 = 3,
		TexCoord4 = 4,
		TexCoord5 = 5,
		TexCoord6 = 6,
		TexCoord7 = 7,
	}
}
