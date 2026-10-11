import 'package:flutter/material.dart';

import '../theme/app_colors.dart';
import '../theme/app_dimens.dart';

class FeedBottomBar extends StatelessWidget {
  const FeedBottomBar({super.key});

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 0, 16, 20),
      child: Row(
        children: [
          Expanded(
            child: Container(
              height: 66,
              padding: const EdgeInsets.symmetric(horizontal: 22),
              decoration: BoxDecoration(
                color: AppColors.surface,
                borderRadius: BorderRadius.circular(AppRadius.pill),
                boxShadow: const [
                  BoxShadow(color: AppColors.shadowColor, blurRadius: 12, offset: Offset(0, 4)),
                ],
              ),
              child: Row(
                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                children: [
                  const _NavIcon(Icons.home_filled, active: true),
                  const _NavIcon(Icons.search),
                  GestureDetector(
                    key: const Key('bottomBarDeviceButton'),
                    onTap: () => Navigator.pushNamed(context, '/device'),
                    child: const _NavIcon(Icons.camera_alt_outlined),
                  ),
                  const _NavIcon(Icons.people_outline),
                ],
              ),
            ),
          ),
          const SizedBox(width: 12),
          Container(
            width: 66,
            height: 66,
            decoration: BoxDecoration(
              color: AppColors.accentTeal,
              shape: BoxShape.circle,
              boxShadow: [
                BoxShadow(
                  color: AppColors.accentTealDark.withValues(alpha: 0.4),
                  blurRadius: 16,
                  offset: const Offset(0, 6),
                ),
              ],
            ),
            child: const Center(
              child: Icon(Icons.add, color: Colors.white, size: 30),
            ),
          ),
        ],
      ),
    );
  }
}

class _NavIcon extends StatelessWidget {
  const _NavIcon(this.icon, {this.active = false});

  final IconData icon;
  final bool active;

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      height: 26,
      child: Stack(
        clipBehavior: Clip.none,
        alignment: Alignment.center,
        children: [
          Icon(
            icon,
            size: 26,
            color: active ? AppColors.textPrimary : AppColors.textSecondary,
          ),
          if (active)
            Positioned(
              bottom: -9,
              child: Container(
                width: 5,
                height: 5,
                decoration: const BoxDecoration(
                  shape: BoxShape.circle,
                  color: AppColors.accentTeal,
                ),
              ),
            ),
        ],
      ),
    );
  }
}
