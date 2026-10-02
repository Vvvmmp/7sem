import 'package:flutter/material.dart';

import '../data/models/story.dart';
import '../theme/app_colors.dart';
import '../theme/app_dimens.dart';
import '../theme/app_text_styles.dart';

class StoryAvatarRow extends StatelessWidget {
  const StoryAvatarRow({super.key, required this.stories});

  final List<StoryItem> stories;

  static const double cardWidth = 108;
  static const double cardHeight = 140;

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      height: 182,
      child: ListView.separated(
        scrollDirection: Axis.horizontal,
        padding: const EdgeInsets.symmetric(horizontal: 16),
        itemCount: stories.length,
        separatorBuilder: (_, _) => const SizedBox(width: 12),
        itemBuilder: (_, index) => _StoryAvatarItem(item: stories[index]),
      ),
    );
  }
}

class _StoryAvatarItem extends StatelessWidget {
  const _StoryAvatarItem({required this.item});

  final StoryItem item;

  @override
  Widget build(BuildContext context) {
    if (item.isAddStory) {
      return SizedBox(
        width: StoryAvatarRow.cardWidth,
        child: Column(
          children: [
            Container(
              width: StoryAvatarRow.cardWidth,
              height: StoryAvatarRow.cardHeight,
              decoration: BoxDecoration(
                color: AppColors.iconCircleBg,
                borderRadius: BorderRadius.circular(AppRadius.card),
              ),
              child: Center(
                child: SizedBox(
                  width: 76,
                  height: 76,
                  child: Stack(
                    clipBehavior: Clip.none,
                    children: [
                      ClipOval(
                        child: Image.asset(
                          item.imageAsset,
                          width: 76,
                          height: 76,
                          fit: BoxFit.cover,
                        ),
                      ),
                      Positioned(
                        left: 23,
                        bottom: -15,
                        child: Container(
                          width: 30,
                          height: 30,
                          decoration: BoxDecoration(
                            color: AppColors.accentTeal,
                            shape: BoxShape.circle,
                            border: Border.all(color: const Color.fromARGB(255, 51, 34, 34), width: 2),
                          ),
                          child: const Icon(Icons.add, size: 16, color: Colors.white),
                        ),
                      ),
                    ],
                  ),
                ),
              ),
            ),
            const SizedBox(height: 8),
            Text('Your Story', style: AppTextStyles.storyLabel),
          ],
        ),
      );
    }

    return Stack(
      clipBehavior: Clip.none,
      children: [
        ClipRRect(
          borderRadius: BorderRadius.circular(AppRadius.card),
          child: Image.asset(
            item.imageAsset,
            width: StoryAvatarRow.cardWidth,
            height: StoryAvatarRow.cardHeight,
            fit: BoxFit.cover,
          ),
        ),
        if (item.avatarAsset != null)
          Positioned(
            top: 8,
            left: 8,
            child: Container(
              width: 34,
              height: 34,
              decoration: BoxDecoration(
                shape: BoxShape.circle,
                border: Border.all(color: Colors.white, width: 2),
              ),
              child: ClipOval(
                child: Image.asset(item.avatarAsset!, fit: BoxFit.cover),
              ),
            ),
          ),
      ],
    );
  }
}
