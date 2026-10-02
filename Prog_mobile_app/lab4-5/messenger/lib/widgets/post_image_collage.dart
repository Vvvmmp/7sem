import 'package:flutter/material.dart';

import '../theme/app_colors.dart';
import '../theme/app_dimens.dart';

class PostImageCollage extends StatelessWidget {
  const PostImageCollage({super.key, required this.imageAssets});

  final List<String> imageAssets;

  @override
  Widget build(BuildContext context) {
    final count = imageAssets.length > 3 ? 3 : imageAssets.length;

    return LayoutBuilder(
      builder: (context, constraints) {
        final maxWidth = constraints.maxWidth;

        if (count >= 3) {
          return _ThreePhotoFan(maxWidth: maxWidth, imageAssets: imageAssets);
        }
        return _StackedPhotos(maxWidth: maxWidth, imageAssets: imageAssets);
      },
    );
  }
}

class _ThreePhotoFan extends StatelessWidget {
  const _ThreePhotoFan({required this.maxWidth, required this.imageAssets});

  final double maxWidth;
  final List<String> imageAssets;

  @override
  Widget build(BuildContext context) {
    final sideWidth = maxWidth * 0.58;
    final sideHeight = sideWidth * 1.3;
    final frontWidth = maxWidth * 0.64;
    final frontHeight = frontWidth * 1.3;
    const frontTop = 28.0;
    final collageHeight = frontTop + frontHeight;

    return SizedBox(
      width: maxWidth,
      height: collageHeight,
      child: Stack(
        clipBehavior: Clip.none,
        children: [
          Positioned(
            left: 0,
            top: 0,
            child: Transform.rotate(
              angle: -0.14,
              child: _Photo(asset: imageAssets[1], width: sideWidth, height: sideHeight),
            ),
          ),
          Positioned(
            right: 0,
            top: 0,
            child: Transform.rotate(
              angle: 0.12,
              child: _Photo(asset: imageAssets[2], width: sideWidth, height: sideHeight),
            ),
          ),
          Positioned(
            left: (maxWidth - frontWidth) / 2,
            top: frontTop,
            child: _Photo(asset: imageAssets[0], width: frontWidth, height: frontHeight),
          ),
        ],
      ),
    );
  }
}

class _StackedPhotos extends StatelessWidget {
  const _StackedPhotos({required this.maxWidth, required this.imageAssets});

  final double maxWidth;
  final List<String> imageAssets;

  @override
  Widget build(BuildContext context) {
    final photoWidth = maxWidth * 0.6;
    final photoHeight = photoWidth * 1.25;
    final step = imageAssets.length > 1 ? maxWidth - photoWidth : 0.0;
    final collageHeight = photoHeight + (imageAssets.length > 1 ? 20 : 0);

    return SizedBox(
      width: maxWidth,
      height: collageHeight,
      child: Stack(
        clipBehavior: Clip.none,
        children: [
          for (var i = 0; i < imageAssets.length; i++)
            Positioned(
              left: step * i,
              top: i == imageAssets.length - 1 ? 20 : 0,
              child: _Photo(asset: imageAssets[i], width: photoWidth, height: photoHeight),
            ),
        ],
      ),
    );
  }
}

class _Photo extends StatelessWidget {
  const _Photo({required this.asset, required this.width, required this.height});

  final String asset;
  final double width;
  final double height;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: width,
      height: height,
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(AppRadius.card),
        boxShadow: const [
          BoxShadow(color: AppColors.shadowColor, blurRadius: 10, offset: Offset(0, 6)),
        ],
      ),
      child: ClipRRect(
        borderRadius: BorderRadius.circular(AppRadius.card),
        child: Image.asset(asset, fit: BoxFit.cover),
      ),
    );
  }
}
