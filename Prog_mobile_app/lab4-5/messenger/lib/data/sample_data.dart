import 'models/post.dart';
import 'models/story.dart';

class SampleData {
  SampleData._();

  static const String profileAvatar = 'assets/images/avatar_me.jpg';

  static const List<StoryItem> stories = [
    StoryItem(
      imageAsset: 'assets/images/avatar_me.jpg',
      name: 'You',
      isAddStory: true,
    ),
    StoryItem(
      imageAsset: 'assets/images/photo_1.jpg',
      avatarAsset: 'assets/images/avatar_1.jpg',
      name: 'Nilesh',
    ),
    StoryItem(
      imageAsset: 'assets/images/photo_2.jpg',
      avatarAsset: 'assets/images/avatar_2.jpg',
      name: 'Anna',
    ),
    StoryItem(
      imageAsset: 'assets/images/photo_3.jpg',
      avatarAsset: 'assets/images/avatar_3.jpg',
      name: 'Arseniy',
    ),
    StoryItem(
      imageAsset: 'assets/images/photo_4.jpg',
      avatarAsset: 'assets/images/avatar_4.jpg',
      name: 'Marya',
    ),
    StoryItem(
      imageAsset: 'assets/images/photo_5.jpg',
      avatarAsset: 'assets/images/avatar_5.jpg',
      name: 'Adam',
    ),
  ];

  static const List<PostItem> feedPosts = [
    PostItem(
      avatarAsset: 'assets/images/avatar_3.jpg',
      username: 'Nilesh',
      meta: 'Posted in u8s · 1h ago',
      captionPrefix: "Discover adventure in patagonia's peaks or serenity provence's ",
      captionMention: '@hamlets',
      captionSuffix: ' - arrival',
      imageAssets: [
        'assets/images/photo_1.jpg',
        'assets/images/photo_2.jpg',
        'assets/images/photo_3.jpg',
      ],
    ),
    PostItem(
      avatarAsset: 'assets/images/avatar_me.jpg',
      username: 'Darlene',
      meta: 'Posted in u8s · 3h ago',
      captionPrefix: 'Golden hour by the coast with ',
      captionMention: '@dw_beats',
      captionSuffix: ' crew - always worth the hike',
      imageAssets: [
        'assets/images/photo_4.jpg',
        'assets/images/photo_1.jpg',
      ],
    ),
  ];

  static const List<PostItem> profilePosts = [
    PostItem(
      avatarAsset: 'assets/images/avatar_3.jpg',
      username: 'Nilesh',
      meta: 'Posted in u8s · 1h ago',
      captionPrefix: "Discover adventure in patagonia's peaks or serenity provence's ",
      captionMention: '@hamlets',
      captionSuffix: ' - arrival',
      imageAssets: [
        'assets/images/photo_2.jpg',
        'assets/images/photo_4.jpg',
        'assets/images/photo_5.jpg',
      ],
    ),
    PostItem(
      avatarAsset: 'assets/images/avatar_me.jpg',
      username: 'Darlene',
      meta: 'Posted in u8s · 3h ago',
      captionPrefix: 'Golden hour by the coast with ',
      captionMention: '@dw_beats',
      captionSuffix: ' crew - always worth the hike',
      imageAssets: [
        'assets/images/photo_4.jpg',
        'assets/images/photo_1.jpg',
        'assets/images/photo_3.jpg',
      ],
    ),
  ];
}
