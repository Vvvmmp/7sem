import 'package:cloud_firestore/cloud_firestore.dart';

class PostItem {
  const PostItem({
    required this.avatarAsset,
    required this.username,
    required this.meta,
    required this.captionPrefix,
    required this.captionMention,
    required this.captionSuffix,
    required this.imageAssets,
    this.id = '',
    this.ownerId = '',
    this.pending = false,
  });

  factory PostItem.fromDoc(DocumentSnapshot<Map<String, dynamic>> doc) {
    final d = doc.data()!;
    return PostItem(
      id: doc.id,
      ownerId: d['ownerId'],
      pending: doc.metadata.hasPendingWrites,
      avatarAsset: d['avatarAsset'],
      username: d['username'],
      meta: d['meta'],
      captionPrefix: d['captionPrefix'],
      captionMention: d['captionMention'],
      captionSuffix: d['captionSuffix'],
      imageAssets: List<String>.from(d['imageAssets']),
    );
  }

  final String id;
  final String ownerId;
  final bool pending;
  final String avatarAsset;
  final String username;
  final String meta;
  final String captionPrefix;
  final String captionMention;
  final String captionSuffix;
  final List<String> imageAssets;

  Map<String, dynamic> toMap() => {
    'avatarAsset': avatarAsset,
    'username': username,
    'meta': meta,
    'captionPrefix': captionPrefix,
    'captionMention': captionMention,
    'captionSuffix': captionSuffix,
    'imageAssets': imageAssets,
  };
}
