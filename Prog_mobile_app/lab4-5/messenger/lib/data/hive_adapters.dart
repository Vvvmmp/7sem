import 'package:hive/hive.dart';

import 'models/app_user.dart';
import 'models/post.dart';

class AppUserAdapter extends TypeAdapter<AppUser> {
  @override
  final int typeId = 0;

  @override
  AppUser read(BinaryReader reader) => AppUser(reader.readString(), reader.readString());

  @override
  void write(BinaryWriter writer, AppUser obj) => writer..writeString(obj.name)..writeString(obj.role);
}

class PostItemAdapter extends TypeAdapter<PostItem> {
  @override
  final int typeId = 1;

  @override
  PostItem read(BinaryReader reader) => PostItem(
        avatarAsset: reader.readString(),
        username: reader.readString(),
        meta: reader.readString(),
        captionPrefix: reader.readString(),
        captionMention: reader.readString(),
        captionSuffix: reader.readString(),
        imageAssets: reader.readStringList(),
      );

  @override
  void write(BinaryWriter writer, PostItem obj) => writer
    ..writeString(obj.avatarAsset)
    ..writeString(obj.username)
    ..writeString(obj.meta)
    ..writeString(obj.captionPrefix)
    ..writeString(obj.captionMention)
    ..writeString(obj.captionSuffix)
    ..writeStringList(obj.imageAssets);
}
