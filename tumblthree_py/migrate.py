import os
import json
import re
from datetime import datetime, timedelta
from .models import db, Blog, File

def parse_ms_date(s):
    """Parses a .NET-style /Date(milliseconds-offset)/ string."""
    if not s:
        return None
    match = re.match(r'/Date\((\d+)([-+]\d{4})?\)/', s)
    if match:
        milliseconds = int(match.group(1))
        # This is a simplification and doesn't handle the timezone offset.
        # It's good enough for this migration.
        return datetime(1970, 1, 1) + timedelta(milliseconds=milliseconds)
    return None

def migrate_data(data_path):
    """Migrates data from the old TumblThree JSON files to the new SQLite database."""
    print(f"Starting migration from path: {data_path}")

    # Find all blog index files. These are files that do not contain '_files'.
    # This is a heuristic and might need adjustment.
    blog_files = [f for f in os.listdir(data_path) if not '_files.' in f and not f.endswith('.bak') and not f.endswith('.new')]

    for blog_file in blog_files:
        blog_filepath = os.path.join(data_path, blog_file)

        with open(blog_filepath, 'r', encoding='utf-8-sig') as f:
            blog_data = json.load(f)

        print(f"Migrating blog: {blog_data.get('Name')}")

        new_blog = Blog(
            name=blog_data.get('Name'),
            url=blog_data.get('Url'),
            location=blog_data.get('Location'),
            child_id=blog_data.get('ChildId'),
            blog_type=blog_data.get('BlogType'),
            original_blog_type=blog_data.get('OriginalBlogType'),
            version=blog_data.get('Version'),
            description=blog_data.get('Description'),
            title=blog_data.get('Title'),
            last_id=blog_data.get('LastId'),
            notes=blog_data.get('Notes'),
            tags=blog_data.get('Tags'),
            rating=blog_data.get('Rating'),
            total_count=blog_data.get('TotalCount'),
            posts=blog_data.get('Posts'),
            texts=blog_data.get('Texts'),
            answers=blog_data.get('Answers'),
            quotes=blog_data.get('Quotes'),
            photos=blog_data.get('Photos'),
            number_of_links=blog_data.get('NumberOfLinks'),
            conversations=blog_data.get('Conversations'),
            videos=blog_data.get('Videos'),
            audios=blog_data.get('Audios'),
            photo_metas=blog_data.get('PhotoMetas'),
            video_metas=blog_data.get('VideoMetas'),
            audio_metas=blog_data.get('AudioMetas'),
            downloaded_texts=blog_data.get('DownloadedTexts'),
            downloaded_quotes=blog_data.get('DownloadedQuotes'),
            downloaded_photos=blog_data.get('DownloadedPhotos'),
            downloaded_links=blog_data.get('DownloadedLinks'),
            downloaded_answers=blog_data.get('DownloadedAnswers'),
            downloaded_conversations=blog_data.get('DownloadedConversations'),
            downloaded_videos=blog_data.get('DownloadedVideos'),
            downloaded_audios=blog_data.get('DownloadedAudios'),
            downloaded_photo_metas=blog_data.get('DownloadedPhotoMetas'),
            downloaded_video_metas=blog_data.get('DownloadedVideoMetas'),
            downloaded_audio_metas=blog_data.get('DownloadedAudioMetas'),
            duplicate_photos=blog_data.get('DuplicatePhotos'),
            duplicate_videos=blog_data.get('DuplicateVideos'),
            duplicate_audios=blog_data.get('DuplicateAudios'),
            download_text=blog_data.get('DownloadText'),
            download_quote=blog_data.get('DownloadQuote'),
            download_photo=blog_data.get('DownloadPhoto'),
            download_link=blog_data.get('DownloadLink'),
            download_answer=blog_data.get('DownloadAnswer'),
            download_conversation=blog_data.get('DownloadConversation'),
            download_video=blog_data.get('DownloadVideo'),
            download_audio=b'DownloadAudio' in blog_data, # older versions might not have this
            create_photo_meta=blog_data.get('CreatePhotoMeta'),
            create_video_meta=blog_data.get('CreateVideoMeta'),
            create_audio_meta=blog_data.get('CreateAudioMeta'),
            download_replies=blog_data.get('DownloadReplies'),
            download_reblogged_posts=blog_data.get('DownloadRebloggedPosts'),
            download_url_list=blog_data.get('DownloadUrlList'),
            dump_crawler_data=blog_data.get('DumpCrawlerData'),
            reg_ex_photos=blog_data.get('RegExPhotos'),
            reg_ex_videos=blog_data.get('RegExVideos'),
            skip_gif=blog_data.get('SkipGif'),
            download_video_thumbnail=blog_data.get('DownloadVideoThumbnail'),
            force_size=blog_data.get('ForceSize'),
            force_rescan=blog_data.get('ForceRescan'),
            check_directory_for_files=blog_data.get('CheckDirectoryForFiles'),
            group_photo_sets=blog_data.get('GroupPhotoSets'),
            save_texts_individual_files=blog_data.get('SaveTextsIndividualFiles'),
            zip_crawler_data=blog_data.get('ZipCrawlerData'),
            download_imgur=blog_data.get('DownloadImgur'),
            download_webmshare=blog_data.get('DownloadWebmshare'),
            download_uguu=blog_data.get('DownloadUguu'),
            download_cat_box=blog_data.get('DownloadCatBox'),
            webmshare_type=blog_data.get('WebmshareType'),
            uguu_type=blog_data.get('UguuType'),
            cat_box_type=blog_data.get('CatBoxType'),
            pnj_download_format=blog_data.get('PnjDownloadFormat'),
            metadata_format=blog_data.get('MetadataFormat'),
            download_pages=blog_data.get('DownloadPages'),
            page_size=blog_data.get('PageSize'),
            download_from=blog_data.get('DownloadFrom'),
            download_to=blog_data.get('DownloadTo'),
            password=blog_data.get('Password'),
            filename_template=blog_data.get('FilenameTemplate'),
            date_added=parse_ms_date(blog_data.get('DateAdded')),
            last_complete_crawl=parse_ms_date(blog_data.get('LastCompleteCrawl')),
            latest_post=parse_ms_date(blog_data.get('LatestPost')),
            online=blog_data.get('Online'),
            settings_tab_index=blog_data.get('SettingsTabIndex'),
            progress=blog_data.get('Progress'),
            collection_id=blog_data.get('CollectionId'),
        )
        db.session.add(new_blog)

        # Now migrate the files
        files_filename = f"{new_blog.name}_files.{new_blog.original_blog_type}"
        files_filepath = os.path.join(data_path, files_filename)

        if os.path.exists(files_filepath):
            with open(files_filepath, 'r', encoding='utf-8-sig') as f:
                files_data = json.load(f)

            if 'Entries' in files_data and files_data['Entries'] is not None:
                for file_entry_data in files_data['Entries']:
                    new_file = File(
                        blog=new_blog,
                        link=file_entry_data.get('L'),
                        original_link=file_entry_data.get('O'),
                        filename=file_entry_data.get('F')
                    )
                    db.session.add(new_file)

    db.session.commit()
    print("Migration completed.")
