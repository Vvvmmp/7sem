import 'package:flutter/material.dart';

import '../data/sample_data.dart';
import '../theme/app_colors.dart';
import '../theme/app_dimens.dart';
import '../theme/app_text_styles.dart';
import '../widgets/feed_bottom_bar.dart';
import '../widgets/post_card.dart';
import '../widgets/story_avatar_row.dart';
import 'profile_screen.dart';

class FeedScreen extends StatelessWidget {
  const FeedScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: Stack(
        children: [
          Positioned(
            top: 0,
            left: 0,
            right: 0,
            height: 340,
            child: Container(
              decoration: const BoxDecoration(
                gradient: LinearGradient(
                  begin: Alignment.topCenter,
                  end: Alignment.bottomCenter,
                  colors: [AppColors.gradientTop, AppColors.gradientBottom],
                ),
              ),
            ),
          ),
          SafeArea(
            bottom: false,
            child: Column(
              children: [
                Padding(
                  padding: const EdgeInsets.symmetric(horizontal: 16),
                  child: Row(
                    children: [
                      const _NotificationPill(count: '3'),
                      const SizedBox(width: 10),
                      const _IconCircleButton(icon: Icons.chat_bubble_outline),
                      const Spacer(),
                      GestureDetector(
                        key: const Key('feedProfileAvatarButton'),
                        onTap: () {
                          Navigator.push(
                            context,
                            MaterialPageRoute(builder: (_) => const ProfileScreen()),
                          );
                        },
                        child: const CircleAvatar(
                          radius: 28,
                          backgroundImage: AssetImage(SampleData.profileAvatar),
                        ),
                      ),
                    ],
                  ),
                ),
                const SizedBox(height: 26),
                Padding(
                  padding: const EdgeInsets.symmetric(horizontal: 16),
                  child: Row(
                    children: [
                      Text('Discover', style: AppTextStyles.tabActive),
                      const SizedBox(width: 20),
                      Text('Following', style: AppTextStyles.tabInactive),
                    ],
                  ),
                ),
                const SizedBox(height: 18),
                StoryAvatarRow(stories: SampleData.stories),
                const SizedBox(height: 20),
                Padding(
                  padding: const EdgeInsets.symmetric(horizontal: 16),
                  child: Row(
                    children: [
                      Text('Recently Post', style: AppTextStyles.sectionHeader),
                      const Spacer(),
                      const Icon(Icons.more_horiz, color: AppColors.textSecondary),
                    ],
                  ),
                ),
                const SizedBox(height: 8),
                Expanded(
                  child: Container(
                    width: double.infinity,
                    margin: const EdgeInsets.symmetric(horizontal: 16),
                    clipBehavior: Clip.antiAlias,
                    decoration: const BoxDecoration(
                      color: AppColors.surface,
                      borderRadius: BorderRadius.all(Radius.circular(28)),
                      boxShadow: [
                        BoxShadow(color: AppColors.shadowColor, blurRadius: 16, offset: Offset(0, 4)),
                      ],
                    ),
                    child: ListView.builder(
                      padding: const EdgeInsets.only(top: 8, bottom: 130),
                      itemCount: SampleData.feedPosts.length,
                      itemBuilder: (_, index) => PostCard(post: SampleData.feedPosts[index]),
                    ),
                  ),
                ),
              ],
            ),
          ),
          const Positioned(
            left: 0,
            right: 0,
            bottom: 0,
            child: SafeArea(top: false, child: FeedBottomBar()),
          ),
        ],
      ),
    );
  }
}

class _IconCircleButton extends StatelessWidget {
  const _IconCircleButton({required this.icon});

  final IconData icon;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: 52,
      height: 52,
      decoration: const BoxDecoration(color: AppColors.iconCircleBg, shape: BoxShape.circle),
      child: Center(child: Icon(icon, size: 22, color: AppColors.textPrimary)),
    );
  }
}

class _NotificationPill extends StatelessWidget {
  const _NotificationPill({required this.count});

  final String count;

  @override
  Widget build(BuildContext context) {
    return Container(
      height: 52,
      padding: const EdgeInsets.symmetric(horizontal: 18),
      decoration: BoxDecoration(
        color: AppColors.iconCircleBg,
        borderRadius: BorderRadius.circular(AppRadius.pill),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          const Icon(Icons.notifications_none, size: 22, color: AppColors.textPrimary),
          const SizedBox(width: 8),
          Text(
            count,
            style: const TextStyle(fontSize: 17, fontWeight: FontWeight.w700, color: AppColors.textPrimary),
          ),
        ],
      ),
    );
  }
}
