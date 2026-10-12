import 'package:flutter/material.dart';

import '../data/models/post.dart';
import '../theme/app_colors.dart';
import '../theme/app_text_styles.dart';

class PostDetailScreen extends StatefulWidget {
  const PostDetailScreen({super.key, required this.post, this.liked = false});

  final PostItem post;
  final bool liked;

  @override
  State<PostDetailScreen> createState() => _PostDetailScreenState();
}

class _PostDetailScreenState extends State<PostDetailScreen> {
  late bool _liked = widget.liked;

  @override
  Widget build(BuildContext context) {
    final post = widget.post;
    return PopScope(
      canPop: false,
      onPopInvokedWithResult: (didPop, _) {
        if (!didPop) Navigator.pop(context, _liked);
      },
      child: Scaffold(
        backgroundColor: AppColors.surface,
        appBar: AppBar(
          backgroundColor: AppColors.surface,
          leading: IconButton(
            key: const Key('postDetailBackButton'),
            icon: const Icon(Icons.arrow_back, color: AppColors.textPrimary),
            onPressed: () => Navigator.pop(context, _liked),
          ),
          title: Text('Post', style: AppTextStyles.sectionHeader),
        ),
        body: ListView(
          padding: const EdgeInsets.all(16),
          children: [
            Row(
              children: [
                CircleAvatar(radius: 26, backgroundImage: AssetImage(post.avatarAsset)),
                const SizedBox(width: 12),
                Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(post.username, style: AppTextStyles.usernameBold),
                    Text(post.meta, style: AppTextStyles.subtextGray),
                  ],
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
            for (final image in post.imageAssets)
              Padding(
                padding: const EdgeInsets.only(bottom: 12),
                child: ClipRRect(
                  borderRadius: BorderRadius.circular(16),
                  child: Image.asset(image, fit: BoxFit.cover),
                ),
              ),
            Align(
              alignment: Alignment.centerLeft,
              child: TextButton.icon(
                key: const Key('postDetailLikeButton'),
                onPressed: () => setState(() => _liked = !_liked),
                icon: Icon(_liked ? Icons.favorite : Icons.favorite_border, color: AppColors.accentTeal),
                label: Text(_liked ? 'Liked' : 'Like', style: AppTextStyles.sectionHeader),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
