#!/usr/bin/env bash
# Copies ~/w/upload (lessons/{lessonId}/pic-N.jpg, poster.jpg) into Blob container lesson-files.
az storage blob upload-batch --account-name googolsst10298 --auth-mode key -d lesson-files -s ~/w/upload --overwrite \
  --content-type image/jpeg -o none && echo UPLOADED $(find ~/w/upload -name '*.jpg' | wc -l) files
