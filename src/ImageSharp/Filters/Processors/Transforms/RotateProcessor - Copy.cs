﻿// <copyright file="RotateProcessor.cs" company="James Jackson-South">
// Copyright (c) James Jackson-South and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace ImageSharp.Processors
{
    using System;
    using System.Numerics;
    using System.Threading.Tasks;

    /// <summary>
    /// Provides methods that allow the rotating of images.
    /// </summary>
    /// <typeparam name="TColor">The pixel format.</typeparam>
    /// <typeparam name="TPacked">The packed format. <example>uint, long, float.</example></typeparam>
    public class RotateProcessor2<TColor, TPacked> : Matrix3x2Processor2<TColor, TPacked>
        where TColor : struct, IPackedPixel<TPacked>
        where TPacked : struct
    {
        /// <summary>
        /// The transform matrix to apply.
        /// </summary>
        private Matrix3x2 processMatrix;

        public RotateProcessor2(IResampler sampler, Rectangle resizeRectangle, float angle)
            : base(sampler, resizeRectangle.Width, resizeRectangle.Height, resizeRectangle)
        {
            this.Angle = angle;
        }

        /// <summary>
        /// Gets or sets the angle of processMatrix in degrees.
        /// </summary>
        public float Angle { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to expand the canvas to fit the rotated image.
        /// </summary>
        public bool Expand { get; set; } = true;

        /// <inheritdoc/>
        protected override void OnApply(ImageBase<TColor, TPacked> source, Rectangle sourceRectangle)
        {
            if (this.OptimizedApply(source))
            {
                return;
            }

            int height = this.CanvasRectangle.Height;
            int width = this.CanvasRectangle.Width;
            Matrix3x2 matrix = this.GetCenteredMatrix(source, this.processMatrix);
            TColor[] target = new TColor[width * height];

            if (this.Sampler is NearestNeighborResampler)
            {
                using (PixelAccessor<TColor, TPacked> sourcePixels = source.Lock())
                using (PixelAccessor<TColor, TPacked> targetPixels = target.Lock<TColor, TPacked>(width, height))
                {
                    Parallel.For(
                        0,
                        height,
                        this.ParallelOptions,
                        y =>
                            {
                                for (int x = 0; x < width; x++)
                                {
                                    Point transformedPoint = Point.Rotate(new Point(x, y), matrix);
                                    if (source.Bounds.Contains(transformedPoint.X, transformedPoint.Y))
                                    {
                                        targetPixels[x, y] = sourcePixels[transformedPoint.X, transformedPoint.Y];
                                    }
                                }
                            });
                }

                source.SetPixels(width, height, target);

                return;
            }

            // Interpolate the image using the calculated weights.
            // TODO: Why is the output here exactly the same as the nearest neighbour??????
            using (PixelAccessor<TColor, TPacked> sourcePixels = source.Lock())
            using (PixelAccessor<TColor, TPacked> targetPixels = target.Lock<TColor, TPacked>(width, height))
            {
                Parallel.For(
                    0,
                    height,
                    this.ParallelOptions,
                    y =>
                    {
                        for (int x = 0; x < width; x++)
                        {
                            Point transformedPoint = Point.Rotate(new Point(x, y), matrix);
                            if (source.Bounds.Contains(transformedPoint.X, transformedPoint.Y))
                            {
                                Weight[] horizontalValues = this.HorizontalWeights[transformedPoint.X].Values;
                                Weight[] verticalValues = this.VerticalWeights[transformedPoint.Y].Values;

                                // Destination color components
                                Vector4 destination = Vector4.Zero;

                                for (int i = 0; i < horizontalValues.Length; i++)
                                {
                                    Weight xw = horizontalValues[i];
                                    destination += sourcePixels[Math.Min(xw.Index, source.Width), transformedPoint.Y].ToVector4() * xw.Value;
                                }

                                for (int i = 0; i < verticalValues.Length; i++)
                                {
                                    Weight yw = verticalValues[i];
                                    destination += sourcePixels[transformedPoint.X, Math.Min(yw.Index, source.Height)].ToVector4() * yw.Value;
                                }

                                TColor d = default(TColor);
                                d.PackFromVector4(destination / 2F);
                                targetPixels[x, y] = d;
                            }
                        }
                    });
            }

            source.SetPixels(width, height, target);
        }

        /// <inheritdoc/>
        protected override void BeforeApply(ImageBase<TColor, TPacked> source, Rectangle sourceRectangle)
        {
            const float Epsilon = .0001F;

            if (Math.Abs(this.Angle) < Epsilon || Math.Abs(this.Angle - 90) < Epsilon || Math.Abs(this.Angle - 180) < Epsilon || Math.Abs(this.Angle - 270) < Epsilon)
            {
                return;
            }

            this.processMatrix = Point.CreateRotation(new Point(0, 0), -this.Angle);
            if (this.Expand)
            {
                this.CreateNewCanvas(sourceRectangle, this.processMatrix);
                this.ResizeRectangle = this.CanvasRectangle;
            }

            base.BeforeApply(source, this.CanvasRectangle);
        }

        /// <summary>
        /// Rotates the images with an optimized method when the angle is 90, 180 or 270 degrees.
        /// </summary>
        /// <param name="source">The source image.</param>
        /// <returns>The <see cref="bool"/></returns>
        private bool OptimizedApply(ImageBase<TColor, TPacked> source)
        {
            const float Epsilon = .0001F;
            if (Math.Abs(this.Angle) < Epsilon)
            {
                // No need to do anything so return.
                return true;
            }

            if (Math.Abs(this.Angle - 90) < Epsilon)
            {
                this.Rotate90(source);
                return true;
            }

            if (Math.Abs(this.Angle - 180) < Epsilon)
            {
                this.Rotate180(source);
                return true;
            }

            if (Math.Abs(this.Angle - 270) < Epsilon)
            {
                this.Rotate270(source);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Rotates the image 270 degrees clockwise at the centre point.
        /// </summary>
        /// <param name="source">The source image.</param>
        private void Rotate270(ImageBase<TColor, TPacked> source)
        {
            int width = source.Width;
            int height = source.Height;
            TColor[] target = new TColor[width * height];

            using (PixelAccessor<TColor, TPacked> sourcePixels = source.Lock())
            using (PixelAccessor<TColor, TPacked> targetPixels = target.Lock<TColor, TPacked>(height, width))
            {
                Parallel.For(
                    0,
                    height,
                    this.ParallelOptions,
                    y =>
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int newX = height - y - 1;
                            newX = height - newX - 1;
                            int newY = width - x - 1;
                            targetPixels[newX, newY] = sourcePixels[x, y];
                        }
                    });
            }

            source.SetPixels(height, width, target);
        }

        /// <summary>
        /// Rotates the image 180 degrees clockwise at the centre point.
        /// </summary>
        /// <param name="source">The source image.</param>
        private void Rotate180(ImageBase<TColor, TPacked> source)
        {
            int width = source.Width;
            int height = source.Height;
            TColor[] target = new TColor[width * height];

            using (PixelAccessor<TColor, TPacked> sourcePixels = source.Lock())
            using (PixelAccessor<TColor, TPacked> targetPixels = target.Lock<TColor, TPacked>(width, height))
            {
                Parallel.For(
                    0,
                    height,
                    this.ParallelOptions,
                    y =>
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int newX = width - x - 1;
                            int newY = height - y - 1;
                            targetPixels[newX, newY] = sourcePixels[x, y];
                        }
                    });
            }

            source.SetPixels(width, height, target);
        }

        /// <summary>
        /// Rotates the image 90 degrees clockwise at the centre point.
        /// </summary>
        /// <param name="source">The source image.</param>
        private void Rotate90(ImageBase<TColor, TPacked> source)
        {
            int width = source.Width;
            int height = source.Height;
            TColor[] target = new TColor[width * height];

            using (PixelAccessor<TColor, TPacked> sourcePixels = source.Lock())
            using (PixelAccessor<TColor, TPacked> targetPixels = target.Lock<TColor, TPacked>(height, width))
            {
                Parallel.For(
                    0,
                    height,
                    this.ParallelOptions,
                    y =>
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int newX = height - y - 1;
                            targetPixels[newX, x] = sourcePixels[x, y];
                        }
                    });
            }

            source.SetPixels(height, width, target);
        }
    }
}