import 'package:flutter/material.dart';

import '../data/models/post.dart';
import '../theme/app_text_styles.dart';
import 'post_image_collage.dart';

class PostCard extends StatelessWidget {
  const PostCard({super.key, required this.post});

  final PostItem post;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              CircleAvatar(radius: 22, backgroundImage: AssetImage(post.avatarAsset)),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      post.username,
                      style: AppTextStyles.usernameBold,
                      overflow: TextOverflow.ellipsis,
                    ),
                    const SizedBox(height: 2),
                    Text(
                      post.meta,
                      style: AppTextStyles.subtextGray,
                      overflow: TextOverflow.ellipsis,
                    ),
                  ],
                ),
              ),
            ],
          ),
          const SizedBox(height: 14),
          Text.rich(
            TextSpan(
              children: [
                TextSpan(text: post.captionPrefix, style: AppTextStyles.captionText),
                TextSpan(text: post.captionMention, style: AppTextStyles.captionMention),
                TextSpan(text: post.captionSuffix, style: AppTextStyles.captionText),
              ],
            ),
          ),
          const SizedBox(height: 16),
          PostImageCollage(imageAssets: post.imageAssets),
        ],
      ),
    );
  }
}
