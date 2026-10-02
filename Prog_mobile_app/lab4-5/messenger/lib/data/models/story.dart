class StoryItem {
  const StoryItem({
    required this.imageAsset,
    required this.name,
    this.avatarAsset,
    this.isAddStory = false,
  });

  final String imageAsset;
  final String name;

  final String? avatarAsset;
  final bool isAddStory;
}
