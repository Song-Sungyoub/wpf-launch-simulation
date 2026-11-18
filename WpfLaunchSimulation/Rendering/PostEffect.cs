using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace WpfLaunchSimulation.Rendering;

// Screen-space finish for the 3D view (WPF 3D has no programmable shading, but a pixel-shader effect can post-process
// the composited image): bloom around bright pixels (Sun, plume, sunlit cloud tops), a gentle contrast/saturation curve
// and a vignette. No depth is available, so nothing here depends on distance. The HLSL is compiled at start-up with
// d3dcompiler_47.dll, which ships with Windows; if that fails the view simply renders without the effect.
public sealed class PostEffect : ShaderEffect
{
    private const string Source = """
        sampler2D input : register(s0);
        float4 ddxDdy : register(c0);     // (ddx of uv, ddy of uv): one pixel step in texture space
        float bloom : register(c1);
        float vignette : register(c2);
        float saturation : register(c3);
        float contrast : register(c4);
        static const float3 Luma = float3(0.299, 0.587, 0.114);

        float4 main(float2 uv : TEXCOORD) : COLOR
        {
            float4 c = tex2D(input, uv);
            float2 px = float2(ddxDdy.x, ddxDdy.w);
            // Weighted neighbourhood for a soft falloff; skipped when bloom is 0 (inset).
            float3 glow = 0;
            [branch] if (bloom > 0.001)
            {
                float weight = 0;
                [unroll] for (int y = -2; y <= 2; y++)
                {
                    [unroll] for (int x = -2; x <= 2; x++)
                    {
                        float w = exp(-0.5 * (x*x + y*y));
                        float3 s = tex2D(input, uv + float2(x, y) * px * 7.0).rgb;
                        glow += s * saturate((dot(s, Luma) - 0.78) * 4.5) * w;
                        weight += w;
                    }
                }
                glow /= weight;
            }
            float3 col = c.rgb + glow * bloom;
            float l = dot(col, Luma);
            col = lerp(l.xxx, col, saturation);
            col = saturate((col - 0.5) * contrast + 0.5 * c.a);   // premultiplied: pivot scales with alpha
            float2 d = uv - 0.5;
            col *= 1 - vignette * dot(d, d) * 1.6;
            float3 added = glow * bloom;
            float alpha = max(c.a, saturate(max(added.r, max(added.g, added.b))));
            return float4(min(col, alpha.xxx), alpha);
        }
        """;

    private static readonly Lazy<PixelShader?> Shader = new(Compile);
    public static bool Available => Shader.Value is not null;

    public PostEffect()
    {
        PixelShader = Shader.Value;
        DdxUvDdyUvRegisterIndex = 0;
        UpdateShaderValue(InputProperty); UpdateShaderValue(BloomProperty); UpdateShaderValue(VignetteProperty);
        UpdateShaderValue(SaturationProperty); UpdateShaderValue(ContrastProperty);
    }

    public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty("Input", typeof(PostEffect), 0);
    public static readonly DependencyProperty BloomProperty = DependencyProperty.Register(nameof(Bloom), typeof(double), typeof(PostEffect), new UIPropertyMetadata(0.35, PixelShaderConstantCallback(1)));
    public static readonly DependencyProperty VignetteProperty = DependencyProperty.Register(nameof(Vignette), typeof(double), typeof(PostEffect), new UIPropertyMetadata(0.22, PixelShaderConstantCallback(2)));
    public static readonly DependencyProperty SaturationProperty = DependencyProperty.Register(nameof(Saturation), typeof(double), typeof(PostEffect), new UIPropertyMetadata(1.08, PixelShaderConstantCallback(3)));
    public static readonly DependencyProperty ContrastProperty = DependencyProperty.Register(nameof(Contrast), typeof(double), typeof(PostEffect), new UIPropertyMetadata(1.06, PixelShaderConstantCallback(4)));
    public Brush Input { get => (Brush)GetValue(InputProperty); set => SetValue(InputProperty, value); }
    public double Bloom { get => (double)GetValue(BloomProperty); set => SetValue(BloomProperty, value); }
    public double Vignette { get => (double)GetValue(VignetteProperty); set => SetValue(VignetteProperty, value); }
    public double Saturation { get => (double)GetValue(SaturationProperty); set => SetValue(SaturationProperty, value); }
    public double Contrast { get => (double)GetValue(ContrastProperty); set => SetValue(ContrastProperty, value); }

    private static PixelShader? Compile()
    {
        try
        {
            var source = Encoding.ASCII.GetBytes(Source);
            int hr = D3DCompile(source, source.Length, "PostEffect", 0, 0, "main", "ps_3_0", OptimizationLevel3, 0, out var code, out var errors);
            if (errors is not null) Marshal.ReleaseComObject(errors);
            if (hr < 0 || code is null) return null;
            var bytes = new byte[(int)code.GetBufferSize()];
            Marshal.Copy(code.GetBufferPointer(), bytes, 0, bytes.Length);
            Marshal.ReleaseComObject(code);
            var shader = new PixelShader(); shader.SetStreamSource(new MemoryStream(bytes)); shader.Freeze();
            return shader;
        }
        catch (Exception) { return null; }   // no d3dcompiler_47.dll, no COM, etc.
    }

    private const uint OptimizationLevel3 = 1 << 15;

    [DllImport("d3dcompiler_47.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern int D3DCompile(byte[] srcData, nint srcDataSize, string? sourceName, nint defines, nint include,
        string entryPoint, string target, uint flags1, uint flags2, out ID3DBlob? code, out ID3DBlob? errorMsgs);

    [ComImport, Guid("8BA5FB08-5195-40e2-AC58-0D989C3A0102"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ID3DBlob
    {
        [PreserveSig] nint GetBufferPointer();
        [PreserveSig] nint GetBufferSize();
    }
}
