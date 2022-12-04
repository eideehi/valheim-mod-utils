using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ModUtils
{
    public class SpriteLoader
    {
        private static readonly Dictionary<string, Texture2D> TextureCache;

        private Logger _logger;

        static SpriteLoader()
        {
            TextureCache = new Dictionary<string, Texture2D>();
        }

        private static Sprite CreateSprite(Texture2D texture, int width, int height)
        {
            return Sprite.Create(texture, new Rect(0, 0, width, height), Vector2.zero);
        }

        public static string GetTextureFileName(Sprite sprite)
        {
            foreach (var pair in TextureCache.Where(pair => pair.Value == sprite.texture))
                return Path.GetFileName(pair.Key);
            return "";
        }

        public void SetDebugLogger(Logger logger)
        {
            _logger = logger;
        }

        public Sprite Load(string texturePath, int width, int height)
        {
            if (!File.Exists(texturePath)) return null;
            if (TextureCache.TryGetValue(texturePath, out var tex))
                return tex != null ? CreateSprite(tex, width, height) : null;

            try
            {
                _logger?.Info($"Try to create sprite: {texturePath}");

                var texture = new Texture2D(0, 0);
                texture.LoadImage(File.ReadAllBytes(texturePath));

                TextureCache.Add(texturePath, texture);
                return CreateSprite(texture, width, height);
            }
            catch (Exception e)
            {
                _logger?.Error($"Failed to create sprite: {texturePath}\n{e}");
                TextureCache.Add(texturePath, null);
                return null;
            }
        }
    }
}