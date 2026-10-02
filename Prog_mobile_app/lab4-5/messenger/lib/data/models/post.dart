class PostItem {
  const PostItem({
    required this.avatarAsset,
    required this.username,
    required this.meta,
    required this.captionPrefix,
    required this.captionMention,
    required this.captionSuffix,
    required this.imageAssets,
  });

  final String avatarAsset;
  final String username;
  final String meta;
  final String captionPrefix;
  final String captionMention;
  final String captionSuffix;
  final List<String> imageAssets;
}
