# YouTube Download (Mac)

## Installation & Setup

### Install [Homebrew](https://brew.sh/)
You'll be using Homebrew to install the YouTube download tool. From your Mac Terminal, enter the following command to install Homebrew:
```bash
/bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"
```
*Pay close attention to the final output of the Homebrew installation, there will be some additional commands it wants you to run.*

### Install [yt-dlp](https://github.com/yt-dlp/yt-dlp)
Run the following from the Terminal prompt:
```bash
brew install yt-dlp
```

### Install [ffmpeg](https://www.ffmpeg.org/)
Run the following from the Terminal prompt to install this additional dependency that `yt-dlp` can use:
```bash
brew install ffmpeg
```

## Using the Tool to Download a Video
1. Copy the URL of the YouTube video you want to download.
2. In the Terminal, navigate to the output folder of your preference. For example:  
```bash
cd Desktop
```
3. Run the following command, pasting in the copied value to replace `URL` (leave the quotation marks):
```bash
yt-dlp --merge-output-format mp4 "URL"
```

