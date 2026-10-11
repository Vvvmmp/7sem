import 'package:flutter/material.dart';

import '../data/models/user_profile.dart';
import '../data/sample_data.dart';
import '../theme/app_colors.dart';
import '../theme/app_text_styles.dart';
import '../widgets/post_card.dart';
import '../widgets/story_avatar_row.dart';

class ProfileScreen extends StatelessWidget {
  const ProfileScreen({super.key, required this.profile});

  final UserProfile profile;

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
            child: Column(
              children: [
                Padding(
                  padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
                  child: Row(
                    children: [
                      IconButton(
                        icon: const Icon(Icons.arrow_back, size: 24, color: AppColors.textPrimary),
                        padding: EdgeInsets.zero,
                        constraints: const BoxConstraints(),
                        onPressed: () => Navigator.pop(context),
                      ),
                      const SizedBox(width: 4),
                      Text(profile.name.split(' ').first, style: AppTextStyles.nameDropdown),
                      const Icon(Icons.keyboard_arrow_down, size: 22, color: AppColors.textPrimary),
                      const Spacer(),
                      OutlinedButton(
                        style: OutlinedButton.styleFrom(
                          shape: const StadiumBorder(),
                          side: const BorderSide(color: AppColors.divider),
                          padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
                          minimumSize: Size.zero,
                          tapTargetSize: MaterialTapTargetSize.shrinkWrap,
                          foregroundColor: AppColors.textPrimary,
                          textStyle: const TextStyle(fontSize: 15, fontWeight: FontWeight.w600),
                        ),
                        onPressed: () {},
                        child: const Text('Edit'),
                      ),
                      const SizedBox(width: 8),
                      const _TwoLineMenuIcon(),
                    ],
                  ),
                ),
                const SizedBox(height: 12),
                CircleAvatar(radius: 60, backgroundImage: AssetImage(profile.avatarAsset)),
                const SizedBox(height: 14),
                Text(profile.name, style: AppTextStyles.profileName),
                const SizedBox(height: 4),
                Text(profile.handle, style: AppTextStyles.profileHandle),
                const SizedBox(height: 20),
                Container(
                  width: double.infinity,
                  padding: const EdgeInsets.symmetric(horizontal: 28, vertical: 16),
                  decoration: const BoxDecoration(
                    border: Border.symmetric(horizontal: BorderSide(color: AppColors.divider)),
                  ),
                  child: const Row(
                    children: [
                      Expanded(child: Center(child: _StatColumn(value: '360', label: 'Post'))),
                      Expanded(child: Center(child: _StatColumn(value: '160k', label: 'Follower'))),
                      Expanded(child: Center(child: _StatColumn(value: '140k', label: 'Following'))),
                    ],
                  ),
                ),
                const SizedBox(height: 12),
                StoryAvatarRow(stories: SampleData.stories),
                const SizedBox(height: 16),
                const Padding(
                  padding: EdgeInsets.symmetric(horizontal: 16),
                  child: _ProfileTabsRow(),
                ),
                const SizedBox(height: 20),
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
                      padding: const EdgeInsets.only(top: 8),
                      itemCount: SampleData.profilePosts.length,
                      itemBuilder: (_, index) => PostCard(post: SampleData.profilePosts[index]),
                    ),
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _TwoLineMenuIcon extends StatelessWidget {
  const _TwoLineMenuIcon();

  @override
  Widget build(BuildContext context) {
    return Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        Container(width: 22, height: 2.4, color: AppColors.textPrimary),
        const SizedBox(height: 5),
        Container(width: 22, height: 2.4, color: AppColors.textPrimary),
      ],
    );
  }
}

class _StatColumn extends StatelessWidget {
  const _StatColumn({required this.value, required this.label});

  final String value;
  final String label;

  @override
  Widget build(BuildContext context) {
    return Column(
      children: [
        Text(value, style: AppTextStyles.statNumber),
        const SizedBox(height: 2),
        Text(label, style: AppTextStyles.statLabel),
      ],
    );
  }
}

class _ProfileTabsRow extends StatelessWidget {
  const _ProfileTabsRow();

  @override
  Widget build(BuildContext context) {
    return const Column(
      children: [
        Row(
          children: [
            Expanded(child: _ProfileTab(icon: Icons.grid_view, label: 'Post', active: true)),
            Expanded(child: _ProfileTab(icon: Icons.alternate_email, label: 'Mention', active: false)),
          ],
        ),
        Row(
          children: [
            Expanded(child: SizedBox(height: 2, child: ColoredBox(color: AppColors.textPrimary))),
            Expanded(child: SizedBox(height: 2, child: ColoredBox(color: AppColors.divider))),
          ],
        ),
      ],
    );
  }
}

class _ProfileTab extends StatelessWidget {
  const _ProfileTab({required this.icon, required this.label, required this.active});

  final IconData icon;
  final String label;
  final bool active;

  @override
  Widget build(BuildContext context) {
    final color = active ? AppColors.textPrimary : AppColors.textSecondary;
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 10),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Icon(icon, size: 20, color: color),
          const SizedBox(width: 6),
          Text(
            label,
            style: TextStyle(
              fontSize: 15,
              color: color,
              fontWeight: active ? FontWeight.w700 : FontWeight.w500,
            ),
          ),
        ],
      ),
    );
  }
}
